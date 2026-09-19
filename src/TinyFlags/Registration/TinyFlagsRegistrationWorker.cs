using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

internal sealed class TinyFlagsRegistrationWorker : BackgroundService
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TinyFlagsClientOptions options;
    private readonly IHostApplicationLifetime lifetime;
    private readonly ILogger<TinyFlagsRegistrationWorker> logger;

    public TinyFlagsRegistrationWorker(TinyFlagsClientOptions options, IHostApplicationLifetime lifetime,
        ILogger<TinyFlagsRegistrationWorker> logger)
    {
        this.options = options.CreateSnapshot();
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
            await RegisterOnceAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled.
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("TinyFlags registration timed out. Local flag values remain available.");
        }
        catch (Exception error)
        {
            // Exception messages and response bodies may contain remote data or credentials.
            logger.LogWarning("TinyFlags registration failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }

    private async Task RegisterOnceAsync(CancellationToken ct)
    {
        var definitions = TinyFlagsBootstrap.GetDefinitions();
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = new TinyFlagsApiClient(http, options);
        using var response = await client.RegisterDefinitionsAsync(definitions, ct).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            logger.LogInformation("TinyFlags definitions registered.");
            return;
        }

        logger.LogWarning("TinyFlags registration returned HTTP {StatusCode}. Local flag values remain available.", (int)response.StatusCode);
    }
}
