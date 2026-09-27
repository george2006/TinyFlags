using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging.Abstractions;

namespace TinyFlags.Tests;

public sealed class TinyFlagsRetryPolicyTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Non_caller_cancellation_retries_even_before_the_attempt_timeout(bool duringRead, bool taskCanceled)
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions
        {
            RetryDelay = TimeSpan.FromMilliseconds(1), MaxRetryDelay = TimeSpan.FromMilliseconds(1)
        });
        var attempts = 0;
        OperationCanceledException failure = taskCanceled ? new TaskCanceledException() : new OperationCanceledException();

        void FailFirstAttempt(CancellationToken token)
        {
            if (attempts == 1)
            {
                Assert.False(token.IsCancellationRequested);
                throw failure;
            }
        }

        var result = await retry.ExecuteAsync(token =>
        {
            attempts++;
            if (!duringRead)
            {
                FailFirstAttempt(token);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, (response, token) =>
        {
            if (duringRead)
            {
                FailFirstAttempt(token);
            }
            return Task.FromResult("recovered");
        }, TimeSpan.FromMinutes(1), NullLogger.Instance, caller.Token);

        Assert.Equal("recovered", result);
        Assert.Equal(2, attempts);
        Assert.False(caller.IsCancellationRequested);
    }

    [Theory]
    [InlineData(false, "cancellation")]
    [InlineData(true, "cancellation")]
    [InlineData(false, "network")]
    [InlineData(true, "network")]
    [InlineData(false, "io")]
    [InlineData(true, "io")]
    [InlineData(false, "unexpected")]
    [InlineData(true, "unexpected")]
    public async Task Caller_cancellation_takes_precedence_over_attempt_failures(bool duringRead, string failureKind)
    {
        using var caller = new CancellationTokenSource();
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        var attempts = 0;
        Exception failure = failureKind switch
        {
            "cancellation" => new OperationCanceledException(),
            "network" => new HttpRequestException(),
            "io" => new IOException(),
            _ => new InvalidOperationException()
        };

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retry.ExecuteAsync<int>(token =>
        {
            attempts++;
            if (!duringRead)
            {
                caller.Cancel();
                throw failure;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, (response, token) =>
        {
            caller.Cancel();
            throw failure;
        }, TimeSpan.FromMinutes(1), NullLogger.Instance, caller.Token));

        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unexpected_failure_propagates_without_retry(bool duringRead)
    {
        using var caller = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        var failure = new InvalidOperationException("Unexpected failure");
        var attempts = 0;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => retry.ExecuteAsync<int>(token =>
        {
            attempts++;
            if (!duringRead)
            {
                throw failure;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, (response, token) => throw failure,
            TimeSpan.FromMinutes(1), NullLogger.Instance, caller.Token));

        Assert.Same(failure, error);
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    [InlineData(599)]
    public void Transient_responses_get_a_positive_bounded_delay(int status)
    {
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        using var response = new HttpResponseMessage((HttpStatusCode)status);

        var delay = retry.GetDelay(response, 0, DateTimeOffset.UtcNow);

        Assert.InRange(delay!.Value, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(204)]
    [InlineData(302)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    [InlineData(413)]
    [InlineData(415)]
    public void Permanent_responses_and_success_do_not_retry_even_with_retry_after(int status)
    {
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60));

        Assert.Null(retry.GetDelay(response, 0, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Backoff_grows_is_jittered_and_stays_capped_through_a_long_outage()
    {
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        var error = new HttpRequestException();
        Assert.InRange(retry.GetDelay(error, 1)!.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2));
        Assert.InRange(retry.GetDelay(error, 2)!.Value, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4));
        var delays = Enumerable.Range(0, 32).Select(_ => retry.GetDelay(error, int.MaxValue)!.Value).ToArray();
        Assert.All(delays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30)));
        Assert.True(delays.Distinct().Count() > 1);
        Assert.Null(retry.GetDelay(new InvalidOperationException("catalog conflict"), 0));
    }

    [Theory]
    [InlineData(429, false)]
    [InlineData(503, false)]
    [InlineData(429, true)]
    [InlineData(503, true)]
    public void Server_minimum_wait_can_exceed_the_normal_cap(int status, bool useDate)
    {
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        var now = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var minimum = TimeSpan.FromDays(90);
        using var response = new HttpResponseMessage((HttpStatusCode)status);
        response.Headers.RetryAfter = useDate
            ? new RetryConditionHeaderValue(now + minimum)
            : new RetryConditionHeaderValue(minimum);

        Assert.Equal(minimum, retry.GetDelay(response, 0, now));
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("Sat, 01 Jan 2000 00:00:00 GMT")]
    public void Invalid_or_expired_retry_after_keeps_backoff(string header)
    {
        var retry = new TinyFlagsRetryPolicy(new TinyFlagsHttpOptions());
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.TryAddWithoutValidation("Retry-After", header);

        Assert.InRange(retry.GetDelay(response, 0, DateTimeOffset.UtcNow)!.Value,
            TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1));
    }
}
