using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TinyFlags;

/// <summary>
/// Owns retry classification and backoff for one request/read pair: which HTTP statuses and
/// exceptions are worth retrying, how long to wait, and honoring a server's Retry-After. The
/// caller only supplies how to send and how to read a response - this owns every attempt after
/// the first.
/// </summary>
internal sealed class TinyFlagsRetryPolicy
{
    // Exponentiating past this would risk overflow in Backoff's Math.Pow(2, attempt) - both call
    // sites below must stay in sync, since they cap the same growing "attempt" value.
    private const int MaxBackoffAttempt = 31;

    private readonly TimeSpan initialDelay;
    private readonly TimeSpan maximumDelay;

    public TinyFlagsRetryPolicy(TinyFlagsHttpOptions options)
    {
        initialDelay = options.RetryDelay;
        maximumDelay = options.MaxRetryDelay;
    }

    public async Task<TResult> ExecuteAsync<TResult>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> read,
        TimeSpan requestTimeout, ILogger logger, CancellationToken callerCancellation)
    {
        var attempt = 0;
        while (true)
        {
            callerCancellation.ThrowIfCancellationRequested();
            var (result, delay) = await SendOnceAsync(send, read, requestTimeout, logger, attempt, callerCancellation).ConfigureAwait(false);
            if (delay is null)
            {
                return result!;
            }

            logger.LogWarning("TinyFlags HTTP request will retry in {Delay}.", delay);
            await WaitForRetryAsync(delay.Value, callerCancellation).ConfigureAwait(false);
            attempt = Math.Min(attempt + 1, MaxBackoffAttempt);
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
        return error is HttpRequestException or IOException ? Backoff(attempt) : null;
    }

    private async Task<(TResult? Result, TimeSpan? Delay)> SendOnceAsync<TResult>(
        Func<CancellationToken, Task<HttpResponseMessage>> send,
        Func<HttpResponseMessage, CancellationToken, Task<TResult>> read,
        TimeSpan requestTimeout, ILogger logger, int attempt, CancellationToken callerCancellation)
    {
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(callerCancellation);
        attemptCancellation.CancelAfter(requestTimeout);
        try
        {
            using var response = await send(attemptCancellation.Token).ConfigureAwait(false);
            attemptCancellation.Token.ThrowIfCancellationRequested();
            var delay = GetDelay(response, attempt, DateTimeOffset.UtcNow);
            if (delay is null)
            {
                var result = await read(response, attemptCancellation.Token).ConfigureAwait(false);
                attemptCancellation.Token.ThrowIfCancellationRequested();
                return (result, null);
            }

            logger.LogWarning("TinyFlags HTTP request returned HTTP {StatusCode}.", (int)response.StatusCode);
            return (default, delay.Value);
        }
        catch (OperationCanceledException error)
        {
            callerCancellation.ThrowIfCancellationRequested();

            // Retry non-caller cancellation, including timeouts outside our linked token.
            var delay = Backoff(attempt);
            logger.LogWarning("TinyFlags HTTP request failed ({ErrorType}).", error.GetType().Name);
            return (default, delay);
        }
        catch (Exception error)
        {
            callerCancellation.ThrowIfCancellationRequested();
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
            initialDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt, 0, MaxBackoffAttempt)));
        return TimeSpan.FromMilliseconds(Math.Max(1, ceiling * (0.5 + Random.Shared.NextDouble() * 0.5)));
    }
}
