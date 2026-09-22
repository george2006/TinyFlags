using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

public sealed class TinyFlagsValuesWatchWorker : BackgroundService
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
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
        using var signal = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        try
        {
            await started.Task.WaitAsync(cancellation.Token).ConfigureAwait(false);
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
