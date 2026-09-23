# Building a transport

A transport is anything that implements one or more of the three contracts in
`TinyFlags`'s `Abstractions/` folder and wires itself up via an extension method on
`TinyFlagsOptions`. `TinyFlags.Http` and `TinyFlags.Grpc` are both real, independent examples of
exactly this — nothing about them is special-cased in core. This page walks through what each
contract asks for, using both as worked examples.

## The three contracts

```csharp
public interface IFeatureDefinitionsTransport
{
    Task RegisterAsync(IReadOnlyList<FeatureDefinition> definitions, CancellationToken ct = default);
}

public interface IFeatureValuesTransport
{
    Task<FeatureValuesResult> GetValuesAsync(IReadOnlyList<FeatureDefinition> catalog,
        FeatureValuesCursor? current, CancellationToken ct = default);
}

public interface IFeatureValuesSubscription
{
    IAsyncEnumerable<FeatureValuesResult> WatchAsync(IReadOnlyList<FeatureDefinition> catalog,
        CancellationToken ct = default);
}
```

You don't have to implement all three. `TinyFlags.Http` implements the first two (registration and
polling); `TinyFlags.Grpc` implements the first and third (registration and real-time push). Mixing
and matching across packages in the same app is also fine — nothing stops you registering via one
transport and watching values via another, since each contract is independently optional.

**Registration** (`IFeatureDefinitionsTransport`) is one-shot: send the catalog, get back success
or a typed failure. There's no pull or push variant because there's nothing to pull or push about
registration — it's called exactly once per host lifetime, at startup, by
`TinyFlagsRegistrationWorker`.

**Pull** (`IFeatureValuesTransport`) answers "what's current, given what I already have." The
`current` parameter is `null` on the very first call; after that, it's whatever
`FeatureValuesCursor` you returned last time. Return `FeatureValuesResult.Unchanged()` when nothing
changed, or `FeatureValuesResult.Updated(cursor, values)` when it did.
`TinyFlagsSynchronizationWorker` owns the polling loop, jitter, and backoff between calls — your
transport only needs to answer one request at a time.

**Push** (`IFeatureValuesSubscription`) is the opposite: you decide when something changed, and you
yield it. A well-behaved implementation never yields `Unchanged()` — there's nothing to report
until something actually changes, so don't bother with the case. `TinyFlagsValuesWatchWorker` just
drains whatever you yield; it doesn't poll, and it doesn't retry your stream if it ends. That's
deliberate: reconnection is your transport's job, not the worker's, because only you know what
"try again" means for your protocol. If your stream drops, reconnect internally and keep yielding
from the caller's perspective — a `foreach` over your `IAsyncEnumerable` should never need to know
a reconnect happened underneath it. `TinyFlags.Grpc`'s `TinyFlagsGrpcTransport.WatchAsync` is a
complete example: it classifies gRPC status codes into transient (reconnect after a delay) versus
permanent (let it propagate and stop the worker), rather than retrying everything indefinitely.

## `FeatureValuesCursor` and `FeatureValuesResult`

```csharp
public sealed class FeatureValuesCursor
{
    public Guid EnvironmentId { get; }
    public long Revision { get; }
}

public sealed class FeatureValuesResult
{
    public FeatureValuesCursor? Cursor { get; }
    public IReadOnlyDictionary<string, object>? Values { get; }
    public bool IsUnchanged => Cursor is null;
}
```

`EnvironmentId` exists so a transport can defend against serving the wrong environment's data —
both reference transports cross-check it on every response they read, not just the first
(`TinyFlagsApiClient` against its ETag, `TinyFlagsGrpcTransport` against `ValuesSnapshot.environment_id`).
That's a real bug class worth guarding against: a misconfigured server, a credential that quietly
started pointing somewhere else mid-connection, a proxy routing to the wrong backend. Whatever your
wire format carries this identity as, check it every time, not just once at connect.

## Errors: `TinyFlagsClientException` and `TinyFlagsClientFailure`

Failures your transport can't recover from on its own should surface as
`TinyFlagsClientException`, carrying one of:

```csharp
public enum TinyFlagsClientFailure
{
    CredentialsRejected,   // the credential itself is invalid
    AccessDenied,          // valid credential, insufficient permission
    DefinitionsConflict,   // registration: a key's kind or default disagrees with what's already registered
    RequestRejected,       // any other rejected request
    InvalidResponse        // anything unclassified, or a response that doesn't parse
}
```

This is the one place worth translating your protocol's native error shape into something
transport-agnostic — the workers, and anything downstream of them, only ever see this enum, never
an HTTP status code or a gRPC `StatusCode`. Both reference transports do this translation at
exactly one point: `TinyFlagsApiClient.RejectedResponse` maps HTTP statuses,
`TinyFlagsGrpcTransport.ToFailure` maps gRPC `StatusCode`s. Compare the two mappings side by side
if you want a sense of how the same five buckets accommodate genuinely different wire protocols.

Anything you *can* recover from — a transient network blip, a dropped connection — shouldn't reach
the worker as an exception at all. Retry it inside your transport instead
(`TinyFlagsRetryPolicy` for HTTP's request-level retries; the reconnect loop in
`TinyFlagsGrpcTransport.WatchAsync` for gRPC's stream-level ones).

## Wiring it up

A transport package contributes an extension method on `TinyFlagsOptions`, not on
`IServiceCollection` directly — that's what lets `AddTinyFlags` stay the one recognizable entry
point regardless of which transport (or transports) a consumer picks, the same way you'd always
recognize `AddMediatR` no matter which handlers you're registering.

```csharp
public static class MyTransportServiceCollectionExtensions
{
    public static TinyFlagsOptions UseMyTransport(this TinyFlagsOptions tinyFlags, Action<MyTransportOptions> configure)
    {
        var services = tinyFlags.Services;
        var options = new MyTransportOptions();
        configure(options);

        services.AddSingleton(provider => new MyTransport(options));
        services.TryAddSingleton<IFeatureDefinitionsTransport>(p => p.GetRequiredService<MyTransport>());
        services.TryAddSingleton<IFeatureValuesTransport>(p => p.GetRequiredService<MyTransport>());

        // Only add the workers whose dependency you actually satisfy - a push-only transport
        // must not add TinyFlagsSynchronizationWorker, and vice versa, since DI would fail
        // resolving a transport that was never registered.
        services.AddHostedService<TinyFlagsRegistrationWorker>();
        services.AddHostedService<TinyFlagsSynchronizationWorker>();

        return tinyFlags;
    }
}
```

Consumers then write:

```csharp
services.AddTinyFlags(tinyFlags => tinyFlags.UseMyTransport(options => { ... }));
```

`TinyFlagsRegistrationWorker`, `TinyFlagsSynchronizationWorker`, and `TinyFlagsValuesWatchWorker`
are all public precisely so a transport package in a different assembly can call
`AddHostedService<T>()` on them — none of the three special-cases which package is calling them.

## Security: the same rule both reference transports enforce

If your transport carries a credential (a Bearer token, an API key, anything a client shouldn't
leak), validate that the endpoint can't send it over plaintext to a non-loopback host. Both
`TinyFlagsHttpOptions` and `TinyFlagsGrpcOptions` enforce this identically: HTTPS is required
unless the endpoint is loopback, in which case plain HTTP/h2c is allowed (useful for local
development and same-host deployments, like a sidecar). Copy that rule rather than reinventing it —
the credential-leak risk is the same regardless of which wire format carries it.

## See also

- [Registration](registration.md), [Value Synchronization](value-synchronization.md) — the
  worker-level behavior every transport plugs into, described transport-agnostically
- [Server Protocol (HTTP)](protocol.md), [Server Protocol (gRPC)](grpc-protocol.md) — the wire
  contracts, if you're implementing the *server* side instead of the client side
- `src/TinyFlags.Http` and `src/TinyFlags.Grpc` — two complete, real implementations to read
  alongside this page
