using System;
using System.Net;
using System.Net.Http;

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

    private TimeSpan Backoff(int attempt)
    {
        // Saturate before exponentiation so a long outage cannot overflow the backoff.
        var ceiling = Math.Min(maximumDelay.TotalMilliseconds,
            initialDelay.TotalMilliseconds * Math.Pow(2, Math.Clamp(attempt, 0, 31)));
        return TimeSpan.FromMilliseconds(Math.Max(1, ceiling * (0.5 + Random.Shared.NextDouble() * 0.5)));
    }
}
