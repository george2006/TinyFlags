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
    private readonly TinyFlagsClientOptions options;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsSynchronizationWorker> logger;

    public TinyFlagsSynchronizationWorker(TinyFlagsApiClient client, FeatureValues values, TinyFlagsClientOptions options,
        IHostApplicationLifetime lifetime, ILogger<TinyFlagsSynchronizationWorker> logger)
    {
        this.client = client;
        this.values = values;
        this.options = options;
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
            await PollAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled, during a request or a wait.
        }
    }

    private async Task PollAsync(CancellationToken ct)
    {
        FeatureSnapshot? accepted = null;
        while (true)
        {
            try
            {
                accepted = await SynchronizeAsync(accepted, ct).ConfigureAwait(false);
            }
            catch (TinyFlagsClientException error) when (error.Failure == TinyFlagsClientFailure.InvalidResponse)
            {
                logger.LogWarning("TinyFlags synchronization received an invalid payload. Retrying on the next refresh.");
            }
            catch (TinyFlagsClientException error)
            {
                logger.LogWarning("TinyFlags synchronization stopped ({Failure}). Local flag values remain available.", error.Failure);
                return;
            }
            catch (OperationCanceledException)
            {
                // Host shutdown is expected; let the caller's cancellation handling take over.
                throw;
            }
            catch (Exception error)
            {
                logger.LogWarning("TinyFlags synchronization failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
                return;
            }

            await Task.Delay(NextRefreshDelay(), ct).ConfigureAwait(false);
        }
    }

    private async Task<FeatureSnapshot?> SynchronizeAsync(FeatureSnapshot? current, CancellationToken ct)
    {
        var result = await client.GetValuesAsync(TinyFlagsBootstrap.GetDefinitions(), current, ct).ConfigureAwait(false);
        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            return current;
        }

        ct.ThrowIfCancellationRequested();
        values.ReplaceSnapshot(snapshot.Values);
        logger.LogInformation("TinyFlags values synchronized at revision {Revision}.", snapshot.Revision);
        return snapshot;
    }

    private TimeSpan NextRefreshDelay()
    {
        // Delay after completion rather than on a fixed timer, so a slow request never causes overlap.
        var positiveJitter = 1.0 + Random.Shared.NextDouble() * 0.1;
        return TimeSpan.FromMilliseconds(options.RefreshInterval.TotalMilliseconds * positiveJitter);
    }
}
