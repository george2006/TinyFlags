using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

internal sealed class TinyFlagsRegistrationWorker : BackgroundService
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TinyFlagsApiClient client;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsRegistrationWorker> logger;

    public TinyFlagsRegistrationWorker(TinyFlagsApiClient client, IHostApplicationLifetime lifetime,
        ILogger<TinyFlagsRegistrationWorker> logger)
    {
        this.client = client;
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
            cancellation.Token.ThrowIfCancellationRequested();
            await RegisterDefinitionsAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled.
        }
        catch (TinyFlagsClientException error)
        {
            logger.LogWarning("TinyFlags registration stopped ({Failure}). Local flag values remain available.", error.Failure);
        }
        catch (Exception error)
        {
            // Exception messages and response bodies may contain remote data or credentials.
            logger.LogWarning("TinyFlags registration failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }

    private async Task RegisterDefinitionsAsync(CancellationToken ct)
    {
        var definitions = TinyFlagsBootstrap.GetDefinitions();
        await client.RegisterDefinitionsAsync(definitions, ct).ConfigureAwait(false);
        logger.LogInformation("TinyFlags definitions registered.");
    }
}
