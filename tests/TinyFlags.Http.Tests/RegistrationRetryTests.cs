using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyFlags.Tests;

public sealed partial class RegistrationWorkerTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(409)]
    public async Task Retry_operation_disposes_every_response_and_returns_only_the_read_result(int finalStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.StatusCode = Interlocked.Increment(ref requests) == 1 ? 503 : finalStatus;
            await context.Response.WriteAsync("response body", context.RequestAborted);
        });
        using var http = new HttpClient();
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions
        {
            RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryDelay = TimeSpan.FromMilliseconds(20)
        });
        var responses = new List<HttpResponseMessage>();

        var final = await retry.ExecuteAsync(async ct =>
        {
            var response = await http.PostAsync(server.Urls.Single() + "/v1/client/definitions", null, ct);
            responses.Add(response);
            return response;
        }, (response, ct) => response.Content.ReadAsStringAsync(ct), TimeSpan.FromSeconds(10), NullLogger.Instance, timeout.Token);

        Assert.Equal(2, requests);
        Assert.Equal((HttpStatusCode)finalStatus, responses[1].StatusCode);
        Assert.Equal("response body", final);
        foreach (var response in responses)
        {
            await Assert.ThrowsAsync<ObjectDisposedException>(() => response.Content.ReadAsStringAsync());
        }
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task Transient_response_retries_the_same_catalog_and_stops_after_success(int status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var retryReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        string? firstBody = null;
        string? secondBody = null;
        await using var server = await StartServerAsync(async context =>
        {
            using var reader = new StreamReader(context.Request.Body);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            if (Interlocked.Increment(ref requests) == 1)
            {
                firstBody = body;
                context.Response.StatusCode = status;
                return;
            }

            secondBody = body;
            retryReceived.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options =>
        {
            Configure(options, server);
            options.RetryDelay = TimeSpan.FromMilliseconds(20);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(40);
        }));
        using var host = builder.Build();
        try
        {
            await host.StartAsync(timeout.Token);
            await retryReceived.Task.WaitAsync(timeout.Token);
            Assert.False(GetWorker(host.Services).ExecuteTask!.IsCompleted);
            Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
            Assert.Equal(2, requests);
            Assert.Equal(firstBody, secondBody);
            release.TrySetResult();
            await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);
            Assert.Equal(2, requests);
            Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task Retry_after_is_observed_before_the_next_http_attempt()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        long firstResponse = 0;
        TimeSpan elapsed = default;
        await using var server = await StartServerAsync(context =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = "1";
                firstResponse = Stopwatch.GetTimestamp();
            }
            else
            {
                elapsed = Stopwatch.GetElapsedTime(firstResponse);
                context.Response.StatusCode = StatusCodes.Status204NoContent;
            }
            return Task.CompletedTask;
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options =>
        {
            Configure(options, server);
            options.RetryDelay = TimeSpan.FromMilliseconds(10);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(20);
        }));
        using var host = builder.Build();

        await host.StartAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(2, requests);
        Assert.True(elapsed >= TimeSpan.FromSeconds(1), $"Retried too early: {elapsed}");
        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shutdown_interrupts_backoff_or_a_very_long_server_delay(bool useRetryAfter)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var responded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            if (useRetryAfter)
            {
                context.Response.Headers.RetryAfter = "7776000"; // 90 days exceeds Task.Delay's range.
            }
            context.Response.OnCompleted(() =>
            {
                responded.TrySetResult();
                return Task.CompletedTask;
            });
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options =>
        {
            Configure(options, server);
            options.RetryDelay = TimeSpan.FromMinutes(1);
            options.MaxRetryDelay = TimeSpan.FromMinutes(1);
        }));
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        await responded.Task.WaitAsync(timeout.Token);

        await host.StopAsync(timeout.Token).WaitAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
    }
}
