# Server protocol (gRPC)

This is `TinyFlags.Grpc`'s wire contract — one of two reference transports, not the only way to
speak to a `TinyFlags.Server`. `TinyFlagsGrpcTransport`, the gRPC client inside the `TinyFlags.Grpc`
package, never references `TinyFlags.Server` directly; it only knows a base `Endpoint` and a
Bearer `ApiKey`. Anything that speaks this contract can stand in for `TinyFlags.Server`: your own
ASP.NET Core app (or any gRPC-capable stack in any language), a different backend entirely, a thin
layer in front of an existing feature-flag store. This page is that contract, written for someone
implementing a gRPC server rather than someone using the SDK.

If you're implementing HTTP instead, the equivalent contract is [Server Protocol (HTTP)](protocol.md)
— the two protocols aren't translations of each other, each is its own complete contract. If
you're implementing the *client* side of either, or a transport that's neither, see
[Building a Transport](building-a-transport.md).

`TinyFlags.Server` is the reference implementation of both, not a requirement — see
[TinyFlags.Server](https://github.com/george2006/TinyFlags.Server) if you'd rather deploy it than
write your own.

## Services

Both defined in [`tinyflags.proto`](../src/TinyFlags.Grpc/Protos/tinyflags.proto), package
`tinyflags.v1`. Two independent services, not one — a server can implement gRPC alone, with no
HTTP transport running at all, and a client can use one service without the other (registration
only, or values only). Neither depends on the other existing.

| Service | Method | Called by | Purpose |
| --- | --- | --- | --- |
| `TinyFlagsDefinitions` | `Register` (unary) | `TinyFlagsRegistrationWorker` | Declare flags and their defaults |
| `TinyFlagsValues` | `Watch` (server streaming) | `TinyFlagsValuesWatchWorker` | Push the current snapshot, then one message per real change |

Every call is authenticated the same way: gRPC call metadata carries
`authorization: Bearer <api-key>` — the same token shape as the HTTP contract, just travelling as
metadata instead of an HTTP header. The token is opaque to the client — it's passed through
verbatim, so your server can issue and validate it however it wants.

### 1. Register definitions

```proto
service TinyFlagsDefinitions {
  rpc Register(RegisterRequest) returns (RegisterResponse);
}

message FeatureDefinition {
  string key = 1;
  FeatureKind kind = 2;
  oneof default_value {
    bool bool_default = 3;
    string string_default = 4;
  }
}

message RegisterRequest {
  repeated FeatureDefinition definitions = 1;
}

message RegisterResponse {}
```

- `kind` is `FEATURE_KIND_BOOLEAN` or `FEATURE_KIND_STRING` — `FEATURE_KIND_UNSPECIFIED` (proto3's
  implicit zero value) is never valid on the wire and should be rejected.
- Exactly one of `bool_default`/`string_default` must be set, matching `kind`. `oneof` makes an
  unset default representable at the protobuf level; a server should reject it rather than assume
  a zero value.
- Success: an empty `RegisterResponse`, no fields to populate. The client never reads anything
  from it beyond the call succeeding.
- One-shot, request/response — registration is inherently that, not a pull or push variant, same
  as the HTTP contract's `POST` endpoint. There's nothing to stream in either direction.

### 2. Watch values (server streaming)

This is the one call the client parses strictly, same spirit as the HTTP contract's conditional
`GET` — `TinyFlagsGrpcTransport` validates every message and cross-checks `environment_id` on
every one, not just the first.

```proto
service TinyFlagsValues {
  rpc Watch(WatchRequest) returns (stream ValuesSnapshot);
}

message WatchRequest {}

message FeatureValue {
  string key = 1;
  FeatureKind kind = 2;
  oneof value {
    bool bool_value = 3;
    string string_value = 4;
  }
}

message ValuesSnapshot {
  string environment_id = 1;
  int64 revision = 2;
  repeated FeatureValue values = 3;
}
```

Requirements the client enforces exactly:

- Send the **current, complete snapshot immediately on connect** — not just future changes. A
  freshly connected client has no prior state to diff against, so the first message must be a full
  picture, the same way the HTTP contract's first `GET` (no `If-None-Match`) always returns `200`
  with a complete body, never `304`.
- After that, send **one message per real change**, each a complete snapshot again (not a diff).
  There is no "unchanged" message on this stream — unlike the HTTP contract's `304`, silence *is*
  "unchanged" here. Don't send a message unless something actually changed.
- `environment_id`: required, a GUID in `"D"` format (e.g.
  `"9857b1a6-40f7-485f-8f3e-e37e8c7d8e7d"`) — the same representation the HTTP contract's `ETag`
  already uses, so a server implementing both can share the formatting logic.
- `revision`: required, a non-negative integer. The client doesn't send a "known revision" back
  (there's no equivalent of `If-None-Match` here — the stream itself carries that role), but it
  does check `revision` alongside `environment_id` as its cursor into `FeatureValuesCursor`. A
  message whose revision is less than or equal to the client's own cursor for that environment is
  silently skipped rather than applied — this matters most right after a reconnect, where a
  lagging replica could otherwise resend a snapshot older than one the client already has and roll
  it backward.
- `values`: each entry needs a non-blank `key`, a `kind` of `FEATURE_KIND_BOOLEAN` or
  `FEATURE_KIND_STRING`, and exactly one of `bool_value`/`string_value` set, matching `kind`. A key
  the client doesn't recognize from its own declared catalog is still accepted — same "valid
  unknown keys are allowed" rule as the HTTP contract — but a *known* key whose kind disagrees with
  the client's own declaration is rejected.
- **The stream never completes on its own.** Hold it open for as long as the client wants updates.
  If your server needs to end a call (shutdown, environment deleted, credential revoked
  mid-stream), end it with a real gRPC status — an unexpected clean completion is treated the same
  as a dropped connection.
- Reconnection after a dropped stream is the *client's* job, not this contract's. A well-behaved
  client reconnects internally and starts over — calling `Watch` again always yields a fresh
  complete snapshot first, exactly like a brand-new connection, never a resumed diff.

## How the client interprets gRPC status codes

`TinyFlagsGrpcTransport` maps `RpcException.StatusCode` the same way `TinyFlagsApiClient` maps
HTTP statuses — this is what a permanent, non-retried failure looks like from your server's status
code alone, on either call:

| `StatusCode` | Client outcome |
| --- | --- |
| `Unauthenticated` | Credentials rejected |
| `PermissionDenied` | Access denied |
| `AlreadyExists` | Definitions conflict (`Register` only — a key's kind or default disagrees with what's already registered) |
| `InvalidArgument`, `ResourceExhausted` | Request rejected |
| Anything else (unclassified, or a malformed message) | Invalid response |

On `Watch` specifically, four codes are treated as **transient** instead — the client reconnects
after `ReconnectDelay` rather than giving up: `Unavailable`, `DeadlineExceeded`, `Internal`,
`Aborted`. Everything else ends the stream permanently through the table above. Choose status
codes deliberately: a code outside the transient set on a genuinely temporary failure (like a
brief database blip) will stop the client's watch loop entirely, the same way an unclassified HTTP
`5xx` outside `408`/`429` would.

## What must match exactly vs. what's yours to design

**Fixed by the client:**
- The two services, their method names, and the message shapes above
- `authorization: Bearer <api-key>` as call metadata, on every call
- `Watch` sending a complete snapshot immediately, then one complete snapshot per real change,
  never a diff and never an "unchanged" message
- The status codes listed above, in the situations described
- `environment_id`'s `"D"`-format GUID string, checked on every `Watch` message

**Entirely up to you:**
- Storage, transactions, concurrency strategy
- What the Bearer token actually is and how you validate it
- Any limits — batch size, key length — the client has no hard-coded expectations about limits,
  only about message shape
- How you detect and publish value changes (`TinyFlags.Server` uses PostgreSQL `LISTEN`/`NOTIFY`;
  a poll loop that only writes to the stream on an actual change works just as well from the
  client's perspective)
- Whether you implement both services, or just one — a registration-only or values-only server is
  a valid, partial implementation

## See also

- [Server Protocol (HTTP)](protocol.md) — the equivalent contract for the polling transport
- [Building a Transport](building-a-transport.md) — the client-side contracts
  (`IFeatureDefinitionsTransport`, `IFeatureValuesSubscription`) this protocol implements
- [TinyFlags.Server](https://github.com/george2006/TinyFlags.Server) — deploying the reference
  implementation itself, including its `LISTEN`/`NOTIFY`-driven push design
