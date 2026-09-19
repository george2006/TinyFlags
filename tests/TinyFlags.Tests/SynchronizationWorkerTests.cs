using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyFlags.Tests;

public sealed class SynchronizationWorkerTests
{
    private const string EnabledKey = "TinyFlags.Tests.Startup.Enabled";
    private const string LabelKey = "TinyFlags.Tests.Startup.Label";

    [Fact]
    public async Task Startup_and_generated_reads_do_not_wait_for_the_first_snapshot()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartServerAsync(async context =>
        {
            received.TrySetResult();
            await release.Task.WaitAsync(context.RequestAborted);
            await WriteSnapshotAsync(context);
        });
        using var host = CreateHost(server);
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();
        var worker = GetWorker(host);
        Assert.Null(worker.ExecuteTask);
        Assert.False(received.Task.IsCompleted);

        try
        {
            await host.StartAsync(timeout.Token).WaitAsync(timeout.Token);
            await received.Task.WaitAsync(timeout.Token);
            Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.False(flags.Enabled);
            Assert.Equal("Local", flags.Label);

            release.TrySetResult();
            await worker.ExecuteTask.WaitAsync(timeout.Token);

            Assert.Same(flags, host.Services.GetRequiredService<StartupFeatureFlags>());
            Assert.True(flags.Enabled);
            Assert.Equal("Remote", flags.Label);
        }
        finally
        {
            release.TrySetResult();
            await host.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task Snapshot_publication_does_not_wait_for_registration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var registrationReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartServerAsync(context => WriteSnapshotAsync(context), async context =>
        {
            registrationReceived.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        using var host = CreateHost(server);
        try
        {
            await host.StartAsync(timeout.Token);
            await registrationReceived.Task.WaitAsync(timeout.Token);
            await GetWorker(host).ExecuteTask!.WaitAsync(timeout.Token);

            var registration = Assert.Single(host.Services.GetServices<IHostedService>().OfType<TinyFlagsRegistrationWorker>());
            Assert.False(registration.ExecuteTask!.IsCompleted);
            Assert.True(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        }
        finally
        {
            await host.StopAsync(timeout.Token);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repeated_configuration_publishes_once_into_the_existing_store_including_empty_snapshots(bool empty)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await WriteSnapshotAsync(context, empty);
        });
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { [EnabledKey] = true, [LabelKey] = "Warm", ["Removed"] = true });
        using var host = CreateHost(server, values, configureTwice: true);
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();

        await host.StartAsync(timeout.Token);
        await GetWorker(host).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.Same(values, host.Services.GetRequiredService<FeatureValues>());
        Assert.Equal(!empty, flags.Enabled);
        Assert.Equal(empty ? "Local" : "Remote", flags.Label);
        Assert.False(values.GetBoolean("Removed", false));
        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(200)]
    [InlineData(304)]
    public async Task Rejected_or_invalid_initial_reads_preserve_existing_values_and_keep_the_host_running(int status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.StatusCode = status;
            if (status == 200)
            {
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync("{", context.RequestAborted);
            }
        });
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { [EnabledKey] = true, [LabelKey] = "Warm" });
        using var host = CreateHost(server, values);

        await host.StartAsync(timeout.Token);
        await GetWorker(host).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.True(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        Assert.Equal("Warm", host.Services.GetRequiredService<StartupFeatureFlags>().Label);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Initial_fetch_retries_a_transient_failure_before_publishing()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                context.Response.StatusCode = 503;
                return;
            }
            await WriteSnapshotAsync(context);
        });
        using var host = CreateHost(server);

        await host.StartAsync(timeout.Token);
        await GetWorker(host).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(2, requests);
        Assert.True(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Stopping_before_the_startup_signal_does_not_fetch_values()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await WriteSnapshotAsync(context);
        });
        using var host = CreateHost(server);
        var worker = GetWorker(host);

        await worker.StartAsync(timeout.Token);
        Assert.False(worker.ExecuteTask!.IsCompleted);
        await worker.StopAsync(timeout.Token);
        await worker.ExecuteTask.WaitAsync(timeout.Token);

        Assert.Equal(0, requests);
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
    }

    [Fact]
    public async Task Shutdown_cancels_an_incomplete_body_without_publishing_partial_values()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartServerAsync(async context =>
        {
            using var signal = context.RequestAborted.Register(() => aborted.TrySetResult());
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync("{", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            received.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { [LabelKey] = "Warm" });
        using var host = CreateHost(server, values);
        await host.StartAsync(timeout.Token);
        await received.Task.WaitAsync(timeout.Token);

        await host.StopAsync(timeout.Token).WaitAsync(timeout.Token);

        await aborted.Task.WaitAsync(timeout.Token);
        await GetWorker(host).ExecuteTask!.WaitAsync(timeout.Token);
        Assert.Equal("Warm", host.Services.GetRequiredService<StartupFeatureFlags>().Label);
    }

    private static TinyFlagsSynchronizationWorker GetWorker(IHost host)
        => Assert.Single(host.Services.GetServices<IHostedService>().OfType<TinyFlagsSynchronizationWorker>());

    private static IHost CreateHost(WebApplication server, FeatureValues? values = null, bool configureTwice = false)
    {
        var builder = Host.CreateApplicationBuilder();
        if (values is not null)
        {
            builder.Services.AddSingleton(values);
        }
        void Configure(TinyFlagsClientOptions options)
        {
            options.Endpoint = new Uri(server.Urls.Single());
            options.ApiKey = "test-key";
            options.RetryDelay = TimeSpan.FromMilliseconds(20);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(40);
        }
        builder.Services.AddTinyFlags(Configure);
        if (configureTwice)
        {
            builder.Services.AddTinyFlags(Configure);
        }
        return builder.Build();
    }

    private static async Task<WebApplication> StartServerAsync(RequestDelegate read, RequestDelegate? register = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var server = builder.Build();
        server.MapGet("/v1/client/values", read);
        server.MapPost("/v1/client/definitions", register ?? (context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        }));
        await server.StartAsync();
        return server;
    }

    private static Task WriteSnapshotAsync(HttpContext context, bool empty = false)
    {
        var revision = empty ? 0 : 1;
        context.Response.Headers.ETag = $"W/\"550e8400-e29b-41d4-a716-446655440000:{revision}\"";
        object[] values = empty ? [] :
        [
            new { key = EnabledKey, kind = "Boolean", value = true },
            new { key = LabelKey, kind = "String", value = "Remote" }
        ];
        return context.Response.WriteAsJsonAsync(new { revision, values }, cancellationToken: context.RequestAborted);
    }
}
