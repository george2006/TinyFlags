# Value synchronization

Once connected to a server, exactly one worker is the only writer to `FeatureValues` — which one
depends on which values contract your transport implements, described in
[Building a Transport](building-a-transport.md):

- **Pull** (`IFeatureValuesTransport`) — `TinyFlagsSynchronizationWorker` fetches once after
  startup, then keeps values fresh on a recurring interval it owns. `TinyFlags.Http` implements
  this.
- **Push** (`IFeatureValuesSubscription`) — `TinyFlagsValuesWatchWorker` drains a stream the
  transport owns; there's no polling interval because there's no polling. `TinyFlags.Grpc`
  implements this.

Both are independent of [registration](registration.md), which only sends definitions and never
touches values — regardless of transport, registration and value synchronization never wait on
each other.

## `TinyFlags.Http`'s wire contract (pull)

`GET /v1/client/values`, authenticated the same way as registration, requires the independent
`values:read` permission:

```http
GET /v1/client/values HTTP/1.1
Authorization: Bearer <client-api-key>
If-None-Match: W/"<environment-id>:3"
```

```json
{"revision": 4, "values": [{"key": "Shop.Checkout.Enabled", "kind": "Boolean", "value": true}]}
```

Responses carry `ETag: W/"<environment-id>:<revision>"` and `Cache-Control: no-store`. Sending that
tag back in `If-None-Match` on the next request returns `304` with no body when the revision has
not advanced — so a normal poll against an unchanged environment costs nothing but a 304.
Authentication and authorization run before conditional matching, including on 304s.

## `TinyFlagsSynchronizationWorker`'s recurring loop (pull)

After `ApplicationStarted`, the worker:

1. Fetches once and publishes the result through `FeatureValues.ReplaceSnapshot`. Existing
   generated flag instances observe the update on their next read — nothing needs to be
   re-resolved.
2. Waits `RefreshInterval` (default 30 seconds) plus 0-10% positive jitter.
3. Fetches again, carrying the last accepted snapshot's cursor, and repeats for the life of the
   host.

The delay happens *after* each attempt completes, never on a fixed timer. There is never more than
one request in flight. `TinyFlags.Http` carries the cursor as an `ETag`/`If-None-Match` pair.

## `TinyFlagsValuesWatchWorker`'s loop (push)

No polling interval, because there's nothing to poll — the worker just drains
`IFeatureValuesSubscription.WatchAsync`'s stream and calls `FeatureValues.ReplaceSnapshot` on each
real update:

1. Subscribes once, after `ApplicationStarted`.
2. `await foreach`s the stream. Each yielded `FeatureValuesResult` republishes immediately —
   there's no delay to wait out, since the transport decides when something changed, not the
   worker.

`TinyFlags.Grpc` implements this over its `Watch` RPC: the server sends the current snapshot
immediately on connect, then a fresh one each time `TinyFlags.Server`'s internal
`TinyEvents → pg_notify → LISTEN` chain observes a change for that environment — real-time in the
tens-of-milliseconds range, not bound by any polling interval. Reconnection after a dropped stream
is the transport's own job (see [Building a Transport](building-a-transport.md)), invisible to
this worker — it just keeps draining the same `IAsyncEnumerable`.

## Failure handling

Both workers follow the same shape: the transport absorbs what it can (retries, reconnects), and
only forwards what it can't as an outcome.

- **Updated or unchanged values** — published or ignored, and the loop/stream continues normally.
- **A recoverable failure** — for pull, a malformed response or an unaccepted 304, logged and
  retried on the next normal cycle, not a second retry loop. For push, a transient stream failure
  the transport reconnects from internally, invisible to the worker.
- **A permanent failure** (rejected credentials, denied access, a rejected request, or anything
  unclassified) — surfaces as `TinyFlagsClientException`, logged, and the loop/stream stops for
  good. The host keeps running with whatever values it last had.

In every case, local flag values never disappear and never throw. An environment that has never
synced successfully falls back to the declared code defaults. One that synced before keeps its
last known values through any later outage.

## Independent of registration

A read-only key can synchronize values even when registration is permanently denied, regardless of
transport. If the first fetch or the first pushed snapshot happens before registration completes,
keys the server does not know about yet simply fall back to their declared defaults until a later
update observes the committed revision — registration and value synchronization never wait on each
other.

## Configuration

`TinyFlagsHttpOptions`, alongside `Endpoint` and `ApiKey`:

| Option | Default | Notes |
| --- | --- | --- |
| `RefreshInterval` | 30 seconds | Base recurring delay, before jitter |
| `MaxSnapshotBytes` | 8 MiB | Bounds both declared content length and bytes actually read |
| `RequestTimeout` | 30 seconds | Covers headers, the complete body, and snapshot parsing |
| `RetryDelay` / `MaxRetryDelay` | 1s / 30s | Exponential backoff bounds for transient failures |

`TinyFlagsGrpcOptions`, alongside `Endpoint` and `ApiKey`:

| Option | Default | Notes |
| --- | --- | --- |
| `ReconnectDelay` | 1 second | Fixed delay before reconnecting a dropped `Watch` stream |

Repeating an equivalent configuration is safe and registers one instance of each worker;
conflicting configuration across calls is rejected.
