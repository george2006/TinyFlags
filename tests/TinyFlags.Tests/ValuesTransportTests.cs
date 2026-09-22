using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace TinyFlags.Tests;

public sealed class ValuesTransportTests
{
    private const string ETag = "W/\"550e8400-e29b-41d4-a716-446655440000:1\"";
    private const string EmptySnapshot = "{\"revision\":1,\"values\":[]}";

    [Theory]
    [InlineData("/prefix", 429)]
    [InlineData("/prefix/", 503)]
    public async Task Fetch_honors_retry_after_and_returns_a_snapshot_after_recovery(string prefix, int status)
    {
        var requests = 0;
        string? authorization = null;
        string? condition = null;
        await using var server = await StartServerAsync(async context =>
        {
            var attempt = Interlocked.Increment(ref requests);
            authorization = context.Request.Headers.Authorization;
            condition = context.Request.Headers.IfNoneMatch;
            if (attempt == 1)
            {
                context.Response.StatusCode = status;
                context.Response.Headers.RetryAfter = "1";
                return;
            }
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag.Replace(":1", ":2");
            await context.Response.WriteAsync(EmptySnapshot.Replace(":1", ":2"), context.RequestAborted);
        }, "/prefix");
        using var http = new HttpClient();
        var sdk = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single() + prefix), ApiKey = "test-key",
            RetryDelay = TimeSpan.FromMilliseconds(10), MaxRetryDelay = TimeSpan.FromMilliseconds(20)
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var elapsed = Stopwatch.StartNew();
        var current = new FeatureSnapshot(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), 1, new());
        var result = await sdk.GetValuesAsync([], current, timeout.Token);

        Assert.Equal(2, Assert.IsType<FeatureSnapshot>(result.Snapshot).Revision);
        Assert.True(elapsed.Elapsed >= TimeSpan.FromMilliseconds(900));
        Assert.Equal(2, requests);
        Assert.Equal("Bearer test-key", authorization);
        Assert.Equal(ETag, condition);
        Assert.False(result.IsUnchanged);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    public async Task Snapshot_limit_counts_actual_utf8_bytes_with_or_without_content_length(bool knownLength, int excessBytes)
    {
        var value = new string('\u00e9', 100);
        var payload = Encoding.UTF8.GetBytes("{\"revision\":1,\"values\":[{\"key\":\"Label\",\"kind\":\"String\",\"value\":\"" + value + "\"}]}");
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag;
            if (knownLength)
            {
                context.Response.ContentLength = payload.Length;
            }
            await context.Response.StartAsync(context.RequestAborted);
            await context.Response.Body.WriteAsync(payload, context.RequestAborted);
        });
        using var http = new HttpClient();
        var sdk = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key",
            MaxSnapshotBytes = payload.Length - excessBytes
        });

        if (excessBytes > 0)
        {
            var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => sdk.GetValuesAsync([]));
            Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
            return;
        }

        var result = await sdk.GetValuesAsync([]);
        var snapshot = Assert.IsType<FeatureSnapshot>(result.Snapshot);
        Assert.Equal(value, snapshot.Values["Label"]);
    }

    [Theory]
    [InlineData("caller")]
    [InlineData("sdk")]
    [InlineData("http")]
    public async Task Cancellation_stops_body_reads_and_timeouts_retry_them_until_a_snapshot_arrives(string source)
    {
        using var testTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(testTimeout.Token);
        var bodyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag;
            if (Interlocked.Increment(ref requests) != 1)
            {
                await context.Response.WriteAsync(EmptySnapshot, context.RequestAborted);
                return;
            }

            using var onAbort = context.RequestAborted.Register(() => disconnected.TrySetResult());
            await context.Response.WriteAsync("{", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            bodyStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        using var http = new HttpClient { Timeout = source == "http" ? TimeSpan.FromSeconds(2) : Timeout.InfiniteTimeSpan };
        var sdk = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key",
            RequestTimeout = TimeSpan.FromSeconds(source == "sdk" ? 2 : 30)
        });

        var pending = sdk.GetValuesAsync([], ct: cancellation.Token);
        await bodyStarted.Task.WaitAsync(testTimeout.Token);
        Assert.False(pending.IsCompleted);
        FeatureValuesResult recovered;
        if (source == "caller")
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(testTimeout.Token));
            recovered = await sdk.GetValuesAsync([], ct: testTimeout.Token);
        }
        else
        {
            recovered = await pending.WaitAsync(testTimeout.Token);
        }
        Assert.Equal(source == "caller", cancellation.IsCancellationRequested);
        await disconnected.Task.WaitAsync(testTimeout.Token);

        Assert.Empty(Assert.IsType<FeatureSnapshot>(recovered.Snapshot).Values);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData("{\"revision\":1,\"values\":[")]
    [InlineData("{\"revision\":1,\"values\":[{\"key\":\"A\",\"kind\":\"Boolean\",\"value\":\"true\"}]}")]
    [InlineData("{\"revision\":2,\"values\":[]}")]
    public async Task Malformed_or_inconsistent_http_payloads_do_not_produce_a_snapshot(string json)
    {
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag;
            await context.Response.WriteAsync(json, context.RequestAborted);
        });
        using var http = new HttpClient();
        var sdk = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key"
        });

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => sdk.GetValuesAsync([]));
        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Interrupted_body_is_retried_inside_the_client_until_a_snapshot_arrives()
    {
        var requests = 0;
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag;
            if (Interlocked.Increment(ref requests) == 1)
            {
                context.Response.ContentLength = 1000;
                await context.Response.WriteAsync("{", context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                context.Abort();
                return;
            }
            await context.Response.WriteAsync(EmptySnapshot, context.RequestAborted);
        });
        using var http = new HttpClient();
        var sdk = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key"
        });

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await sdk.GetValuesAsync([], ct: timeout.Token);

        Assert.Empty(Assert.IsType<FeatureSnapshot>(result.Snapshot).Values);
        Assert.Equal(2, requests);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(2L)]
    public async Task Only_newer_snapshots_are_reported_as_updates(long serverRevision)
    {
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag.Replace(":1", ":" + serverRevision);
            await context.Response.WriteAsync(EmptySnapshot.Replace(":1", ":" + serverRevision), context.RequestAborted);
        });
        using var http = new HttpClient();
        using var client = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key"
        });
        var current = new FeatureSnapshot(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), 1,
            new() { ["Existing"] = true });

        var result = await client.GetValuesAsync([], current);

        Assert.Equal(serverRevision <= current.Revision, result.IsUnchanged);
        if (serverRevision > current.Revision)
        {
            Assert.Equal(serverRevision, Assert.IsType<FeatureSnapshot>(result.Snapshot).Revision);
        }
        else
        {
            Assert.Null(result.Snapshot);
        }
        Assert.True(Assert.IsType<bool>(current.Values["Existing"]));
    }

    [Theory]
    [InlineData(false, "matching")]
    [InlineData(true, "missing")]
    [InlineData(true, "wrong-revision")]
    [InlineData(true, "wrong-environment")]
    [InlineData(true, "matching")]
    public async Task Unchanged_requires_a_matching_accepted_snapshot(bool hasCurrent, string tagKind)
    {
        await using var server = await StartServerAsync(context =>
        {
            context.Response.StatusCode = 304;
            if (tagKind != "missing")
            {
                context.Response.Headers.ETag = tagKind switch
                {
                    "wrong-revision" => ETag.Replace(":1", ":2"),
                    "wrong-environment" => ETag.Replace("550e8400", "650e8400"),
                    _ => ETag
                };
            }
            return Task.CompletedTask;
        });
        using var http = new HttpClient();
        using var client = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key"
        });
        var current = hasCurrent ? new FeatureSnapshot(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), 1, new()) : null;

        if (hasCurrent && tagKind == "matching")
        {
            var result = await client.GetValuesAsync([], current);
            Assert.True(result.IsUnchanged);
            Assert.Null(result.Snapshot);
        }
        else
        {
            var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => client.GetValuesAsync([], current));
            Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
        }
    }

    [Fact]
    public async Task Changed_response_cannot_switch_the_accepted_environment()
    {
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag.Replace("550e8400", "650e8400").Replace(":1", ":2");
            await context.Response.WriteAsync(EmptySnapshot.Replace(":1", ":2"), context.RequestAborted);
        });
        using var http = new HttpClient();
        using var client = CreateClient(http, new TinyFlagsClientOptions
        {
            Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key"
        });
        var current = new FeatureSnapshot(Guid.Parse("550e8400-e29b-41d4-a716-446655440000"), 1, new());

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => client.GetValuesAsync([], current));

        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Injected_reader_controls_the_download_limit()
    {
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "application/json";
            context.Response.Headers.ETag = ETag;
            await context.Response.WriteAsync(EmptySnapshot, context.RequestAborted);
        });
        using var http = new HttpClient();
        var options = new TinyFlagsClientOptions { Endpoint = new Uri(server.Urls.Single()), ApiKey = "test-key" };
        using var client = new TinyFlagsApiClient(http, options, new FeatureSnapshotReader(1));

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => client.GetValuesAsync([]));

        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    private static async Task<WebApplication> StartServerAsync(RequestDelegate handler, string prefix = "")
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var server = builder.Build();
        server.MapGet(prefix + "/v1/client/values", handler);
        await server.StartAsync();
        return server;
    }
    private static TinyFlagsApiClient CreateClient(HttpClient http, TinyFlagsClientOptions options)
        => new(http, options, new FeatureSnapshotReader(options.MaxSnapshotBytes));
}
