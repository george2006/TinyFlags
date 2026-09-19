using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyFlags.Tests;

public sealed partial class RegistrationWorkerTests
{
    [Fact]
    public async Task Host_starts_and_serves_generated_defaults_while_registration_is_blocked()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            received.TrySetResult(body.RootElement.Clone());
            await release.Task.WaitAsync(context.RequestAborted);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        builder.Services.AddTinyFlags(options => Configure(options, server));
        await using var app = builder.Build();
        app.MapGet("/defaults", (StartupFeatureFlags flags) => new { flags.Enabled, flags.Label });
        var worker = GetWorker(app.Services);
        Assert.Null(worker.ExecuteTask);
        Assert.False(received.Task.IsCompleted);
        Assert.False(app.Services.GetRequiredService<StartupFeatureFlags>().Enabled);

        try
        {
            await app.StartAsync(timeout.Token).WaitAsync(timeout.Token);
            var request = await received.Task.WaitAsync(timeout.Token);
            Assert.False(worker.ExecuteTask!.IsCompleted);
            Assert.True(app.Lifetime.ApplicationStarted.IsCancellationRequested);
            using var browser = new HttpClient();
            var defaults = await browser.GetFromJsonAsync<JsonElement>(app.Urls.Single() + "/defaults", timeout.Token);
            Assert.False(defaults.GetProperty("enabled").GetBoolean());
            Assert.Equal("Local", defaults.GetProperty("label").GetString());
            var definitions = request.GetProperty("definitions").EnumerateArray().ToDictionary(item => item.GetProperty("key").GetString()!);
            Assert.False(definitions["TinyFlags.Tests.Startup.Enabled"].GetProperty("defaultValue").GetBoolean());
            Assert.Equal("Local", definitions["TinyFlags.Tests.Startup.Label"].GetProperty("defaultValue").GetString());

            release.TrySetResult();
            await worker.ExecuteTask.WaitAsync(timeout.Token);
            Assert.Equal(1, requests);
            Assert.False(app.Lifetime.ApplicationStopping.IsCancellationRequested);
            Assert.False(app.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        }
        finally
        {
            release.TrySetResult();
            await app.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task Repeated_equivalent_configuration_sends_once_and_preserves_an_explicit_store()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(context =>
        {
            Interlocked.Increment(ref requests);
            Assert.Equal("Bearer test-key", context.Request.Headers.Authorization.ToString());
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var builder = Host.CreateApplicationBuilder();
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { ["TinyFlags.Tests.Startup.Enabled"] = true });
        builder.Services.AddSingleton(values);
        builder.Services.AddTinyFlags();
        TinyFlagsClientOptions? captured = null;
        builder.Services.AddTinyFlags(options =>
        {
            Configure(options, server);
            captured = options;
        });
        captured!.ApiKey = "mutated-key";
        captured.Endpoint = new Uri("https://wrong.example.com");
        captured.RequestTimeout = TimeSpan.Zero;
        builder.Services.AddTinyFlags(options =>
        {
            Configure(options, server);
            options.Endpoint = new Uri(options.Endpoint!.AbsoluteUri.TrimEnd('/') + "/");
        });
        builder.Services.AddTinyFlags();
        using var host = builder.Build();

        await host.StartAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.Same(values, host.Services.GetRequiredService<FeatureValues>());
        Assert.True(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData("endpoint")]
    [InlineData("key")]
    [InlineData("timeout")]
    [InlineData("retry")]
    [InlineData("maxRetry")]
    public void Conflicting_configuration_is_rejected_without_changing_the_original_settings(string difference)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options =>
        {
            options.Endpoint = new Uri("https://flags.example.com/base");
            options.ApiKey = "original-secret";
        });

        var error = Assert.Throws<InvalidOperationException>(() => builder.Services.AddTinyFlags(options =>
        {
            options.Endpoint = new Uri(difference == "endpoint" ? "https://other.example.com" : "https://flags.example.com/base/");
            options.ApiKey = difference == "key" ? "other-secret" : "original-secret";
            options.RequestTimeout = TimeSpan.FromSeconds(difference == "timeout" ? 10 : 30);
            options.RetryDelay = TimeSpan.FromSeconds(difference == "retry" ? 2 : 1);
            options.MaxRetryDelay = TimeSpan.FromSeconds(difference == "maxRetry" ? 60 : 30);
        }));

        Assert.DoesNotContain("original-secret", error.Message);
        Assert.DoesNotContain("other-secret", error.Message);
        using var host = builder.Build();
        var settings = host.Services.GetRequiredService<TinyFlagsClientOptions>();
        Assert.Equal(new Uri("https://flags.example.com/base/"), settings.Endpoint);
        Assert.Equal("original-secret", settings.ApiKey);
        Assert.Equal(TimeSpan.FromSeconds(30), settings.RequestTimeout);
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
    }

    [Fact]
    public async Task Invalid_configuration_is_rejected_during_setup_and_local_only_registration_still_works()
    {
        var builder = Host.CreateApplicationBuilder();
        Assert.Throws<ArgumentException>(() => builder.Services.AddTinyFlags(options =>
        {
            options.Endpoint = new Uri("http://remote.example.com");
            options.ApiKey = "test-key";
        }));
        builder.Services.AddTinyFlags();
        using var host = builder.Build();

        await host.StartAsync();

        Assert.Empty(host.Services.GetServices<IHostedService>().OfType<TinyFlagsRegistrationWorker>());
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        await host.StopAsync();
    }

    [Fact]
    public async Task Stopping_the_worker_before_the_startup_signal_completes_without_sending()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options => Configure(options, server));
        using var host = builder.Build();
        var worker = GetWorker(host.Services);

        await worker.StartAsync(timeout.Token);
        Assert.False(worker.ExecuteTask!.IsCompleted);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStarted.IsCancellationRequested);
        await worker.StopAsync(timeout.Token);
        await worker.ExecuteTask.WaitAsync(timeout.Token);

        Assert.Equal(0, requests);
    }

    [Fact]
    public async Task Host_shutdown_cancels_an_in_flight_registration()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var aborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await StartServerAsync(async context =>
        {
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            using var registration = context.RequestAborted.Register(() => aborted.TrySetResult());
            received.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options => Configure(options, server));
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        await received.Task.WaitAsync(timeout.Token);

        await host.StopAsync(timeout.Token).WaitAsync(timeout.Token);

        await aborted.Task.WaitAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(415)]
    [InlineData(302)]
    [InlineData(200)]
    public async Task Failed_responses_complete_one_attempt_without_stopping_the_host_or_following_redirects(int status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.StatusCode = status;
            context.Response.Headers.Location = "/v1/client/definitions";
            return Task.CompletedTask;
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options => Configure(options, server));
        using var host = builder.Build();

        await host.StartAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.Equal("Local", host.Services.GetRequiredService<StartupFeatureFlags>().Label);
        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Network_failure_or_timeout_recovers_without_stopping_the_host(bool useTimeout)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            var attempt = Interlocked.Increment(ref requests);
            using var body = await JsonDocument.ParseAsync(context.Request.Body);
            if (attempt > 1)
            {
                context.Response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
            if (useTimeout)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
            }
            else
            {
                context.Abort();
            }
        });
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags(options =>
        {
            Configure(options, server);
            options.RequestTimeout = TimeSpan.FromSeconds(2);
            options.RetryDelay = TimeSpan.FromMilliseconds(20);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(40);
        });
        using var host = builder.Build();

        await host.StartAsync(timeout.Token);
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(timeout.Token);

        Assert.Equal(2, requests);
        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        Assert.False(host.Services.GetRequiredService<StartupFeatureFlags>().Enabled);
        await host.StopAsync(timeout.Token);
    }

    private static TinyFlagsRegistrationWorker GetWorker(IServiceProvider services)
        => Assert.Single(services.GetServices<IHostedService>().OfType<TinyFlagsRegistrationWorker>());

    private static void Configure(TinyFlagsClientOptions options, WebApplication server)
    {
        options.Endpoint = new Uri(server.Urls.Single());
        options.ApiKey = "test-key";
    }

    private static async Task<WebApplication> StartServerAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var server = builder.Build();
        server.MapPost("/v1/client/definitions", handler);
        await server.StartAsync();
        return server;
    }
}

public sealed class Startup : IFeatureProvider
{
    public bool Enabled => false;
    public string Label => "Local";
}
