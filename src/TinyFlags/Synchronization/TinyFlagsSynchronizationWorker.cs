using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

/// <summary>
/// Drains a <b>pull</b> transport (<see cref="IFeatureValuesTransport"/>): fetches once after
/// startup, then repeats on a recurring, jittered <see cref="FeatureValuesPollingOptions.RefreshInterval"/>
/// - the only writer to <see cref="FeatureValues"/> when the configured transport is pull-based.
/// A push-only transport must not register this worker; see
/// <see cref="TinyFlagsValuesWatchWorker"/> for that side. Public, not internal: a transport
/// package in a different assembly needs to call
/// <c>AddHostedService&lt;TinyFlagsSynchronizationWorker&gt;()</c> on it.
/// </summary>
public sealed class TinyFlagsSynchronizationWorker : BackgroundService
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly IFeatureValuesTransport transport;
    private readonly FeatureValues values;
    private readonly FeatureValuesPollingOptions options;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsSynchronizationWorker> logger;

    public TinyFlagsSynchronizationWorker(IFeatureValuesTransport transport, FeatureValues values, FeatureValuesPollingOptions options,
        IHostApplicationLifetime lifetime, ILogger<TinyFlagsSynchronizationWorker> logger)
    {
        this.transport = transport;
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
        FeatureValuesCursor? accepted = null;
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

    private async Task<FeatureValuesCursor?> SynchronizeAsync(FeatureValuesCursor? current, CancellationToken ct)
    {
        var result = await transport.GetValuesAsync(TinyFlagsBootstrap.GetDefinitions(), current, ct).ConfigureAwait(false);
        if (result.IsUnchanged)
        {
            return current;
        }

        ct.ThrowIfCancellationRequested();
        values.ReplaceSnapshot(result.Values!);
        logger.LogInformation("TinyFlags values synchronized at revision {Revision}.", result.Cursor!.Revision);
        return result.Cursor;
    }

    private TimeSpan NextRefreshDelay()
    {
        // Delay after completion rather than on a fixed timer, so a slow request never causes overlap.
        var positiveJitter = 1.0 + Random.Shared.NextDouble() * 0.1;
        return TimeSpan.FromMilliseconds(options.RefreshInterval.TotalMilliseconds * positiveJitter);
    }
}
