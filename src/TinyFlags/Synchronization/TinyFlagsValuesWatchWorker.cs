using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

/// <summary>
/// Applies value updates after host startup. The transport owns reconnection.
/// Remains public for compatibility with direct worker registration.
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
            // Host shutdown is expected, including before startup completes.
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
            throw;
        }
        catch (Exception error)
        {
            logger.LogWarning("TinyFlags subscription failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }
}
