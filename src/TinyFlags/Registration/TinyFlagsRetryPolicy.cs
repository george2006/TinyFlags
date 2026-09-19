using System;
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

    public async Task<HttpResponseMessage> ExecuteAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, ILogger logger, CancellationToken ct)
    {
        var attempt = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var (response, delay) = await SendOnceAsync(send, logger, attempt, ct).ConfigureAwait(false);
            if (response is not null)
            {
                return response; // The caller owns the final response.
            }

            logger.LogWarning("TinyFlags HTTP request will retry in {Delay}.", delay);
            await WaitForRetryAsync(delay, ct).ConfigureAwait(false);
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
        return error is HttpRequestException or OperationCanceledException ? Backoff(attempt) : null;
    }

    private async Task<(HttpResponseMessage? Response, TimeSpan Delay)> SendOnceAsync(
        Func<CancellationToken, Task<HttpResponseMessage>> send, ILogger logger, int attempt, CancellationToken ct)
    {
        HttpResponseMessage? response = null;
        try
        {
            response = await send(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var delay = GetDelay(response, attempt, DateTimeOffset.UtcNow);
            if (delay is null)
            {
                var finalResponse = response;
                response = null; // Transfer ownership; the finally block disposes retry responses only.
                return (finalResponse, TimeSpan.Zero);
            }

            logger.LogWarning("TinyFlags HTTP request returned HTTP {StatusCode}.", (int)response.StatusCode);
            return (null, delay.Value);
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
            return (null, delay.Value);
        }
        finally
        {
            response?.Dispose();
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
