using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

internal sealed class TinyFlagsSynchronizationWorker : BackgroundService
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TinyFlagsApiClient client;
    private readonly FeatureValues values;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsSynchronizationWorker> logger;

    public TinyFlagsSynchronizationWorker(TinyFlagsApiClient client, FeatureValues values,
        IHostApplicationLifetime lifetime, ILogger<TinyFlagsSynchronizationWorker> logger)
    {
        this.client = client;
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
            await SynchronizeAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled.
        }
        catch (TinyFlagsClientException error)
        {
            logger.LogWarning("TinyFlags synchronization stopped ({Failure}). Local flag values remain available.", error.Failure);
        }
        catch (Exception error)
        {
            logger.LogWarning("TinyFlags synchronization failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }

    private async Task SynchronizeAsync(CancellationToken ct)
    {
        var result = await client.GetValuesAsync(TinyFlagsBootstrap.GetDefinitions(), ct: ct).ConfigureAwait(false);
        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            return;
        }

        ct.ThrowIfCancellationRequested();
        values.ReplaceSnapshot(snapshot.Values);
        logger.LogInformation("TinyFlags values synchronized at revision {Revision}.", snapshot.Revision);
    }
}
