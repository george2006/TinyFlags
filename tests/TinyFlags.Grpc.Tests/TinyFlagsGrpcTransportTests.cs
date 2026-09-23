using System.Runtime.CompilerServices;
using Grpc.Core;
using TinyFlags.Grpc.TestServer;

namespace TinyFlags.Tests;

/// <summary>
/// Exercises TinyFlagsGrpcTransport against a real gRPC server (TinyFlags.Grpc.TestServer), not a
/// mock - the same philosophy TinyFlags.Http.Tests already uses against a real loopback Kestrel
/// server. TinyFlags.Grpc's client-only codegen means the fake server has to live in its own
/// project (see that project's csproj comment); everything here only ever touches core types.
/// </summary>
public sealed class TinyFlagsGrpcTransportTests
{
    [Fact]
    public async Task Register_sends_definitions_and_succeeds()
    {
        await using var server = await FakeTinyFlagsServer.StartAsync();
        IReadOnlyList<FeatureDefinition>? received = null;
        server.OnRegister = definitions =>
        {
            received = definitions;
            return null;
        };
        using var transport = CreateTransport(server);

        await transport.RegisterAsync([FeatureDefinition.Boolean("A.B", true), FeatureDefinition.String("C.D", "x")]);

        Assert.NotNull(received);
        Assert.Equal(2, received!.Count);
        Assert.Equal("A.B", received[0].Key);
        Assert.Equal(FeatureKind.Boolean, received[0].Kind);
        Assert.Equal(true, received[0].DefaultValue);
        Assert.Equal("C.D", received[1].Key);
        Assert.Equal(FeatureKind.String, received[1].Kind);
        Assert.Equal("x", received[1].DefaultValue);
    }

    [Theory]
    [InlineData(StatusCode.Unauthenticated, TinyFlagsClientFailure.CredentialsRejected)]
    [InlineData(StatusCode.PermissionDenied, TinyFlagsClientFailure.AccessDenied)]
    [InlineData(StatusCode.AlreadyExists, TinyFlagsClientFailure.DefinitionsConflict)]
    [InlineData(StatusCode.InvalidArgument, TinyFlagsClientFailure.RequestRejected)]
    [InlineData(StatusCode.ResourceExhausted, TinyFlagsClientFailure.RequestRejected)]
    [InlineData(StatusCode.Unknown, TinyFlagsClientFailure.InvalidResponse)]
    public async Task Register_maps_status_codes_to_failures(StatusCode statusCode, TinyFlagsClientFailure expected)
    {
        await using var server = await FakeTinyFlagsServer.StartAsync();
        server.OnRegister = _ => statusCode;
        using var transport = CreateTransport(server);

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => transport.RegisterAsync([]));

        Assert.Equal(expected, error.Failure);
    }

    [Fact]
    public async Task Watch_yields_the_initial_snapshot_then_real_updates()
    {
        var environmentId = Guid.NewGuid().ToString("D");
        var secondReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var server = await FakeTinyFlagsServer.StartAsync();

        async IAsyncEnumerable<(string, long, IReadOnlyList<(string, object)>)> Stream([EnumeratorCancellation] CancellationToken ct)
        {
            yield return (environmentId, 1, [("A", true)]);
            await secondReady.Task.WaitAsync(ct);
            yield return (environmentId, 2, [("A", false)]);
        }
        server.OnWatch = Stream;

        using var transport = CreateTransport(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.False(enumerator.Current.IsUnchanged);
        Assert.Equal(1, enumerator.Current.Cursor!.Revision);
        Assert.Equal(true, enumerator.Current.Values!["A"]);

        secondReady.TrySetResult();
        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Cursor!.Revision);
        Assert.Equal(false, enumerator.Current.Values!["A"]);
    }

    [Fact]
    public async Task Watch_reconnects_after_a_transient_status_and_resends_a_full_snapshot()
    {
        var environmentId = Guid.NewGuid().ToString("D");
        var attempts = 0;
        await using var server = await FakeTinyFlagsServer.StartAsync();

        async IAsyncEnumerable<(string, long, IReadOnlyList<(string, object)>)> Stream([EnumeratorCancellation] CancellationToken ct)
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                yield return (environmentId, 1, []);
                throw new FakeGrpcStatusException(StatusCode.Unavailable);
            }
            yield return (environmentId, 2, []);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }
        server.OnWatch = Stream;

        using var transport = CreateTransport(server, reconnectDelay: TimeSpan.FromMilliseconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(1, enumerator.Current.Cursor!.Revision);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(2, enumerator.Current.Cursor!.Revision);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task Watch_stops_permanently_on_a_non_transient_status()
    {
        var attempts = 0;
        await using var server = await FakeTinyFlagsServer.StartAsync();
        server.OnWatch = _ =>
        {
            Interlocked.Increment(ref attempts);
            throw new FakeGrpcStatusException(StatusCode.PermissionDenied);
        };

        using var transport = CreateTransport(server, reconnectDelay: TimeSpan.FromMilliseconds(10));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => enumerator.MoveNextAsync().AsTask());

        Assert.Equal(TinyFlagsClientFailure.AccessDenied, error.Failure);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Watch_rejects_a_message_from_a_different_environment_mid_stream()
    {
        var first = Guid.NewGuid().ToString("D");
        var second = Guid.NewGuid().ToString("D");
        await using var server = await FakeTinyFlagsServer.StartAsync();

        async IAsyncEnumerable<(string, long, IReadOnlyList<(string, object)>)> Stream([EnumeratorCancellation] CancellationToken ct)
        {
            yield return (first, 1, []);
            yield return (second, 2, []);
            await Task.CompletedTask;
        }
        server.OnWatch = Stream;

        using var transport = CreateTransport(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal(first, enumerator.Current.Cursor!.EnvironmentId.ToString("D"));

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => enumerator.MoveNextAsync().AsTask());
        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    public async Task Watch_rejects_a_malformed_environment_id(string environmentId)
    {
        await using var server = await FakeTinyFlagsServer.StartAsync();

        async IAsyncEnumerable<(string, long, IReadOnlyList<(string, object)>)> Stream([EnumeratorCancellation] CancellationToken ct)
        {
            yield return (environmentId, 1, []);
            await Task.CompletedTask;
        }
        server.OnWatch = Stream;

        using var transport = CreateTransport(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => enumerator.MoveNextAsync().AsTask());
        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Watch_rejects_duplicate_keys_in_one_snapshot()
    {
        var environmentId = Guid.NewGuid().ToString("D");
        await using var server = await FakeTinyFlagsServer.StartAsync();

        async IAsyncEnumerable<(string, long, IReadOnlyList<(string, object)>)> Stream([EnumeratorCancellation] CancellationToken ct)
        {
            yield return (environmentId, 1, [("A", true), ("A", false)]);
            await Task.CompletedTask;
        }
        server.OnWatch = Stream;

        using var transport = CreateTransport(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        var error = await Assert.ThrowsAsync<TinyFlagsClientException>(() => enumerator.MoveNextAsync().AsTask());
        Assert.Equal(TinyFlagsClientFailure.InvalidResponse, error.Failure);
    }

    [Fact]
    public async Task Watch_completes_cleanly_when_the_stream_ends_with_no_messages()
    {
        await using var server = await FakeTinyFlagsServer.StartAsync();
        // server.OnWatch left null: ValuesService.Watch returns immediately, ending the call cleanly.
        using var transport = CreateTransport(server);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await using var enumerator = transport.WatchAsync([]).GetAsyncEnumerator(timeout.Token);

        Assert.False(await enumerator.MoveNextAsync());
    }

    private static TinyFlagsGrpcTransport CreateTransport(FakeTinyFlagsServer server, TimeSpan? reconnectDelay = null)
        => new(new TinyFlagsGrpcOptions
        {
            Endpoint = server.Endpoint,
            ApiKey = "test-key",
            ReconnectDelay = reconnectDelay ?? TimeSpan.FromSeconds(1)
        });
}
