using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

internal sealed class TinyFlagsRetryPolicy
{
    private readonly TimeSpan initialDelay;
    private readonly TimeSpan maximumDelay;

    public TinyFlagsRetryPolicy(TinyFlagsClientOptions options)
    {
        initialDelay = options.RetryDelay;
        maximumDelay = options.MaxRetryDelay;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> read,
        TimeSpan requestTimeout, ILogger logger, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (result, delay) = await SendOnceAsync(send, read, requestTimeout, logger, attempt, ct).ConfigureAwait(false);
            if (delay is null)
            {
                return result!;
            }

            logger.LogWarning("TinyFlags HTTP request will retry in {Delay}.", delay);
            await WaitForRetryAsync(delay.Value, ct).ConfigureAwait(false);
            attempt = Math.Min(attempt + 1, 31);
        }
    }

    public TimeSpan? GetDelay(HttpResponseMessage response, int attempt, DateTimeOffset now)
    {
        var status = response.StatusCode;
        if (status != HttpStatusCode.RequestTimeout && status != HttpStatusCode.TooManyRequests
            && ((int)status < 500 || (int)status > 599))
        {
            return null;
        }

        var delay = Backoff(attempt);
        if (status == HttpStatusCode.TooManyRequests || status == HttpStatusCode.ServiceUnavailable)
        {
            var retryAfter = response.Headers.RetryAfter;
            var serverDelay = retryAfter?.Delta ?? (retryAfter?.Date - now);
            if (serverDelay > delay)
            {
                return serverDelay;
            }
        }

        return delay;
    }

    public TimeSpan? GetDelay(Exception error, int attempt)
    {
        return error is HttpRequestException or IOException or OperationCanceledException ? Backoff(attempt) : null;
    }

    private async Task<(TResult? Result, TimeSpan? Delay)> SendOnceAsync<TResult>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> read,
        TimeSpan requestTimeout, ILogger logger, int attempt, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(requestTimeout);
        try
        {
            using var response = await send(timeout.Token).ConfigureAwait(false);
            timeout.Token.ThrowIfCancellationRequested();
            var delay = GetDelay(response, attempt, DateTimeOffset.UtcNow);
            if (delay is null)
            {
                var result = await read(response, timeout.Token).ConfigureAwait(false);
                timeout.Token.ThrowIfCancellationRequested();
                return (result, null);
            }

            logger.LogWarning("TinyFlags HTTP request returned HTTP {StatusCode}.", (int)response.StatusCode);
            return (default, delay.Value);
        }
        catch (Exception error)
        {
            ct.ThrowIfCancellationRequested();
            var delay = GetDelay(error, attempt);
            if (delay is null)
            {
                throw;
            }

            logger.LogWarning("TinyFlags HTTP request failed ({ErrorType}).", error.GetType().Name);
            return (default, delay.Value);
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

    private TimeSpan Backoff(int attempt)
    {
        // Saturate before exponentiation so a long outage cannot overflow the backoff.
        var ceiling = Math.Min(maximumDelay.TotalMilliseconds,
            initialDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt, 0, 31)));
        return TimeSpan.FromMilliseconds(Math.Max(1, ceiling * (0.5 + Random.Shared.NextDouble() * 0.5)));
    }
}
