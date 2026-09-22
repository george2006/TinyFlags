using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace TinyFlags.Tests;

public sealed class ValuesWatchWorkerTests
{
    [Fact]
    public async Task Applies_each_update_in_order_and_skips_unchanged()
    {
        var environmentId = Guid.NewGuid();
        var subscription = new FakeValuesSubscription(
            FeatureValuesResult.Updated(new FeatureValuesCursor(environmentId, 1), new Dictionary<string, object> { ["A"] = true }),
            FeatureValuesResult.Unchanged(),
            FeatureValuesResult.Updated(new FeatureValuesCursor(environmentId, 2), new Dictionary<string, object> { ["A"] = false }));
        using var host = CreateHost(subscription);

        await host.StartAsync();
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(host.Services.GetRequiredService<FeatureValues>().GetBoolean("A", true));
        await host.StopAsync();
    }

    [Fact]
    public async Task Host_shutdown_cancels_an_in_flight_watch_without_crashing()
    {
        var subscription = new FakeValuesSubscription(blockForever: true);
        using var host = CreateHost(subscription);
        var worker = GetWorker(host.Services);

        await host.StartAsync();
        await subscription.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(worker.ExecuteTask!.IsCompleted);

        await host.StopAsync();

        await worker.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task A_transport_failure_stops_the_worker_without_crashing_the_host()
    {
        var subscription = new FakeValuesSubscription(failure: new TinyFlagsClientException(TinyFlagsClientFailure.CredentialsRejected));
        using var host = CreateHost(subscription);

        await host.StartAsync();
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync();
    }

    [Fact]
    public async Task An_unexpected_exception_stops_the_worker_without_crashing_the_host()
    {
        var subscription = new FakeValuesSubscription(failure: new InvalidOperationException("boom"));
        using var host = CreateHost(subscription);

        await host.StartAsync();
        await GetWorker(host.Services).ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.IsCancellationRequested);
        await host.StopAsync();
    }

    private static TinyFlagsValuesWatchWorker GetWorker(IServiceProvider services)
        => Assert.Single(services.GetServices<IHostedService>().OfType<TinyFlagsValuesWatchWorker>());

    private static IHost CreateHost(IFeatureValuesSubscription subscription)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddTinyFlags();
        builder.Services.AddSingleton(subscription);
        builder.Services.AddHostedService<TinyFlagsValuesWatchWorker>();
        return builder.Build();
    }

    private sealed class FakeValuesSubscription(
        IReadOnlyList<FeatureValuesResult>? results = null, bool blockForever = false, Exception? failure = null)
        : IFeatureValuesSubscription
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public FakeValuesSubscription(params FeatureValuesResult[] results) : this((IReadOnlyList<FeatureValuesResult>)results) { }

        public async IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            Started.TrySetResult();
            foreach (var result in results ?? [])
            {
                yield return result;
            }
            if (failure is not null)
            {
                throw failure;
            }
            if (blockForever)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
        }
    }
}
