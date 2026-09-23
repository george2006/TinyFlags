using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

/// <summary>
/// Drains a <b>push</b> transport (<see cref="IFeatureValuesSubscription"/>): subscribes once
/// after startup and republishes every yielded result immediately - the only writer to
/// <see cref="FeatureValues"/> when the configured transport is push-based. There is no polling
/// loop here; reconnecting a dropped stream is the transport's own job, invisible to this worker.
/// A pull-only transport must not register this worker; see
/// <see cref="TinyFlagsSynchronizationWorker"/> for that side. Public, not internal: a transport
/// package in a different assembly needs to call
/// <c>AddHostedService&lt;TinyFlagsValuesWatchWorker&gt;()</c> on it.
/// </summary>
public sealed class TinyFlagsValuesWatchWorker : BackgroundService
{
    private readonly IFeatureValuesSubscription subscription;
    private readonly FeatureValues values;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsValuesWatchWorker> logger;

    public TinyFlagsValuesWatchWorker(IFeatureValuesSubscription subscription, FeatureValues values,
        IHostApplicationLifetime lifetime, ILogger<TinyFlagsValuesWatchWorker> logger)
    {
        this.subscription = subscription;
        this.values = values;
        this.lifetime = lifetime;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        try
        {
            await lifetime.WaitForApplicationStartedAsync(cancellation.Token).ConfigureAwait(false);
            await WatchAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled, while watching.
        }
    }

    private async Task WatchAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var result in subscription.WatchAsync(TinyFlagsBootstrap.GetDefinitions(), ct).ConfigureAwait(false))
            {
                if (result.IsUnchanged)
                {
                    continue;
                }

                values.ReplaceSnapshot(result.Values!);
                logger.LogInformation("TinyFlags values synchronized at revision {Revision}.", result.Cursor!.Revision);
            }
        }
        catch (TinyFlagsClientException error)
        {
            logger.LogWarning("TinyFlags subscription stopped ({Failure}). Local flag values remain available.", error.Failure);
        }
        catch (OperationCanceledException)
        {
            // Host shutdown is expected; let the caller's cancellation handling take over.
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning("TinyFlags subscription failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }
}
