using System;
using System.Collections.Generic;
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
            await RegisterUntilCompleteAsync(cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Host shutdown is expected, including before ApplicationStarted is signalled.
        }
        catch (Exception error)
        {
            // Exception messages and response bodies may contain remote data or credentials.
            logger.LogWarning("TinyFlags registration failed ({ErrorType}). Local flag values remain available.", error.GetType().Name);
        }
    }

    private async Task RegisterUntilCompleteAsync(CancellationToken ct)
    {
        var definitions = TinyFlagsBootstrap.GetDefinitions();
        using var http = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var client = new TinyFlagsApiClient(http, options);
        var retry = new TinyFlagsRetryPolicy(options);
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var delay = await RegisterOnceAsync(client, definitions, retry, attempt, ct).ConfigureAwait(false);
            if (delay is null)
            {
                return;
            }

            logger.LogWarning("TinyFlags registration will retry in {Delay}. Local flag values remain available.", delay.Value);
            await WaitForRetryAsync(delay.Value, ct).ConfigureAwait(false);
            attempt = Math.Min(attempt + 1, 31);
        }
    }

    private async Task<TimeSpan?> RegisterOnceAsync(TinyFlagsApiClient client, IReadOnlyList<FeatureDefinition> definitions,
        TinyFlagsRetryPolicy retry, int attempt, CancellationToken ct)
    {
        try
        {
            using var response = await client.RegisterDefinitionsAsync(definitions, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                logger.LogInformation("TinyFlags definitions registered.");
                return null;
            }

            logger.LogWarning("TinyFlags registration returned HTTP {StatusCode}.", (int)response.StatusCode);
            return retry.GetDelay(response, attempt, DateTimeOffset.UtcNow);
        }
        catch (Exception error)
        {
            ct.ThrowIfCancellationRequested();
            var delay = retry.GetDelay(error, attempt);
            if (delay is null)
            {
                throw;
            }

            logger.LogWarning("TinyFlags registration attempt failed ({ErrorType}).", error.GetType().Name);
            return delay;
        }
    }

    private static async Task WaitForRetryAsync(TimeSpan delay, CancellationToken ct)
    {
        // Retry-After may exceed Task.Delay's limit. Preserve the server's minimum wait.
        var chunk = TimeSpan.FromDays(1);
        while (delay > chunk)
        {
            await Task.Delay(chunk, ct).ConfigureAwait(false);
            delay -= chunk;
        }
        await Task.Delay(delay, ct).ConfigureAwait(false);
    }
}
