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
            await WaitUntilAsync(() => flags.Enabled, timeout.Token);

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
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();
        try
        {
            await host.StartAsync(timeout.Token);
            await registrationReceived.Task.WaitAsync(timeout.Token);
            await WaitUntilAsync(() => flags.Enabled, timeout.Token);

            var registration = Assert.Single(host.Services.GetServices<IHostedService>().OfType<TinyFlagsRegistrationWorker>());
            Assert.False(registration.ExecuteTask!.IsCompleted);
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
        await WaitUntilAsync(() => flags.Label == (empty ? "Local" : "Remote"), timeout.Token);

        Assert.Equal(1, requests);
        Assert.Same(values, host.Services.GetRequiredService<FeatureValues>());
        Assert.Equal(!empty, flags.Enabled);
        Assert.False(values.GetBoolean("Removed", false));
        await host.StopAsync(timeout.Token);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Permanent_failures_stop_the_refresh_loop_and_keep_the_host_running(int status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(context =>
        {
            Interlocked.Increment(ref requests);
            context.Response.StatusCode = status;
            return Task.CompletedTask;
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

    [Theory]
    [InlineData(200)]
    [InlineData(304)]
    public async Task Invalid_payloads_preserve_values_and_recover_on_a_later_refresh(int firstStatus)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            if (Interlocked.Increment(ref requests) == 1)
            {
                context.Response.StatusCode = firstStatus;
                if (firstStatus == 200)
                {
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{", context.RequestAborted);
                }
                return;
            }
            await WriteSnapshotAsync(context);
        });
        var values = new FeatureValues();
        values.ReplaceSnapshot(new Dictionary<string, object> { [EnabledKey] = true, [LabelKey] = "Warm" });
        using var host = CreateHost(server, values, refreshInterval: TimeSpan.FromMilliseconds(20));
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();

        await host.StartAsync(timeout.Token);
        Assert.Equal("Warm", flags.Label);
        await WaitUntilAsync(() => flags.Label == "Remote", timeout.Token);

        Assert.True(requests >= 2);
        Assert.True(flags.Enabled);
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
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();

        await host.StartAsync(timeout.Token);
        await WaitUntilAsync(() => flags.Enabled, timeout.Token);

        Assert.Equal(2, requests);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Recurring_refresh_fetches_again_and_publishes_a_newer_revision()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        string? secondRequestConditionalHeader = null;
        await using var server = await StartServerAsync(async context =>
        {
            var attempt = Interlocked.Increment(ref requests);
            if (attempt == 2)
            {
                secondRequestConditionalHeader = context.Request.Headers.IfNoneMatch.ToString();
            }
            await WriteSnapshotAsync(context, revision: attempt, label: attempt == 1 ? "Remote" : "RemoteAgain");
        });
        using var host = CreateHost(server, refreshInterval: TimeSpan.FromMilliseconds(20));
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();

        await host.StartAsync(timeout.Token);
        await WaitUntilAsync(() => flags.Label == "RemoteAgain", timeout.Token);

        Assert.True(requests >= 2);
        Assert.Equal("W/\"550e8400-e29b-41d4-a716-446655440000:1\"", secondRequestConditionalHeader);
        await host.StopAsync(timeout.Token);
    }

    [Fact]
    public async Task Shutdown_during_the_refresh_wait_stops_without_a_further_request()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            Interlocked.Increment(ref requests);
            await WriteSnapshotAsync(context);
        });
        using var host = CreateHost(server);
        var flags = host.Services.GetRequiredService<StartupFeatureFlags>();

        await host.StartAsync(timeout.Token);
        await WaitUntilAsync(() => flags.Enabled, timeout.Token);
        Assert.Equal(1, requests);
        var worker = GetWorker(host);
        Assert.False(worker.ExecuteTask!.IsCompleted);

        await host.StopAsync(timeout.Token).WaitAsync(timeout.Token);

        Assert.Equal(1, requests);
        Assert.True(worker.ExecuteTask.IsCompleted);
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

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    private static TinyFlagsSynchronizationWorker GetWorker(IHost host)
        => Assert.Single(host.Services.GetServices<IHostedService>().OfType<TinyFlagsSynchronizationWorker>());

    private static IHost CreateHost(WebApplication server, FeatureValues? values = null, bool configureTwice = false,
        TimeSpan? refreshInterval = null)
    {
        var builder = Host.CreateApplicationBuilder();
        if (values is not null)
        {
            builder.Services.AddSingleton(values);
        }
        void Configure(TinyFlagsHttpOptions options)
        {
            options.Endpoint = new Uri(server.Urls.Single());
            options.ApiKey = "test-key";
            options.RetryDelay = TimeSpan.FromMilliseconds(20);
            options.MaxRetryDelay = TimeSpan.FromMilliseconds(40);
            options.RefreshInterval = refreshInterval ?? TimeSpan.FromSeconds(10);
        }
        builder.Services.UseHttpTransport(Configure);
        if (configureTwice)
        {
            builder.Services.UseHttpTransport(Configure);
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

    private static Task WriteSnapshotAsync(HttpContext context, bool empty = false, long revision = 1, string label = "Remote")
    {
        var effectiveRevision = empty ? 0 : revision;
        context.Response.Headers.ETag = $"W/\"550e8400-e29b-41d4-a716-446655440000:{effectiveRevision}\"";
        object[] values = empty ? [] :
        [
            new { key = EnabledKey, kind = "Boolean", value = true },
            new { key = LabelKey, kind = "String", value = label }
        ];
        return context.Response.WriteAsJsonAsync(new { revision = effectiveRevision, values }, cancellationToken: context.RequestAborted);
    }
}
