# Value synchronization

Once connected to a server, `TinyFlagsSynchronizationWorker` is the SDK's only writer to
`FeatureValues`. It fetches the environment's values once after startup, then keeps them fresh on
a recurring interval — independent of [registration](registration.md), which only sends
definitions and never touches values.

## HTTP contract

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

## The recurring loop

After `ApplicationStarted`, the worker:

1. Fetches once and publishes the result through `FeatureValues.ReplaceSnapshot`. Existing
   generated flag instances observe the update on their next read — nothing needs to be
   re-resolved.
2. Waits `RefreshInterval` (default 30 seconds) plus 0-10% positive jitter.
3. Fetches again, carrying the last accepted snapshot's `ETag`, and repeats for the life of the
   host.

The delay happens *after* each attempt completes, never on a fixed timer. There is never more than
one request in flight.

## Failure handling

Every fetch goes through the same `TinyFlagsRetryPolicy` used by registration, so transient
network errors, timeouts, and 408/429/5xx responses (including `Retry-After`) are already retried
before the worker ever sees an outcome. What reaches the worker is one of:

- **Updated or unchanged values** — published or ignored, and the loop continues normally.
- **A recoverable failure** (a malformed response body, or a 304 with no snapshot accepted yet,
  which is a protocol error rather than empty values) — logged, and retried on the next normal
  cycle. This is not a second retry loop; it just waits for the regular interval.
- **A permanent failure** (rejected credentials, denied access, a rejected request, or anything
  unclassified) — logged, and the loop stops for good. The host keeps running with whatever values
  it last had.

In every case, local flag values never disappear and never throw. An environment that has never
synced successfully falls back to the declared code defaults. One that synced before keeps its
last known values through any later outage.

## Independent of registration

A read-only key (`values:read` without `definitions:register`) can synchronize values even when
registration is permanently denied. If the first fetch happens before registration completes,
keys the server does not know about yet simply fall back to their declared defaults until a later
poll observes the committed revision — registration and synchronization never wait on each other.

## Configuration

All on `TinyFlagsClientOptions`, alongside `Endpoint` and `ApiKey`:

| Option | Default | Notes |
| --- | --- | --- |
| `RefreshInterval` | 30 seconds | Base recurring delay, before jitter |
| `MaxSnapshotBytes` | 8 MiB | Bounds both declared content length and bytes actually read |
| `RequestTimeout` | 30 seconds | Covers headers, the complete body, and snapshot parsing |
| `RetryDelay` / `MaxRetryDelay` | 1s / 30s | Exponential backoff bounds for transient failures |

Repeating an equivalent configuration is safe and registers one instance of each worker;
conflicting configuration across calls is rejected.
