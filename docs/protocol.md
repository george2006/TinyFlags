# Server protocol (HTTP)

This is `TinyFlags.Http`'s wire contract — one of two reference transports, not the only way to
speak to a `TinyFlags.Server`. `TinyFlagsApiClient`, the HTTP client inside the `TinyFlags.Http`
package, never references `TinyFlags.Server` directly; it only knows a base `Endpoint`, a Bearer
`ApiKey`, and three routes. Anything that speaks this contract can stand in for `TinyFlags.Server`:
your own ASP.NET Core app, a different language entirely, a thin layer in front of an existing
feature-flag store. This page is that contract, written for someone implementing an HTTP server
rather than someone using the SDK.

If you're implementing gRPC instead, the equivalent contract is `tinyflags.proto`
(`src/TinyFlags.Grpc/Protos/tinyflags.proto`), not this page — the two protocols aren't
translations of each other, each is its own complete contract. If you're implementing the *client*
side of either, or a transport that's neither, see [Building a Transport](building-a-transport.md).

`TinyFlags.Server` is the reference implementation of both, not a requirement — see
[TinyFlags.Server](https://github.com/george2006/TinyFlags.Server) if you'd rather deploy it than
write your own.

## Endpoints

All three are relative to `TinyFlagsHttpOptions.Endpoint` and authenticated the same way:
`Authorization: Bearer <api-key>`. The token is opaque to the client — it's passed through
verbatim, so your server can issue and validate it however it wants.

| Method & path | Called by | Purpose |
| --- | --- | --- |
| `POST v1/client/definitions` | `TinyFlagsRegistrationWorker` | Declare flags and their defaults |
| `GET v1/client/values` | `TinyFlagsSynchronizationWorker` | Conditional GET, revisioned polling |
| `PATCH v1/client/values` | Nothing in the SDK | Write access for external tooling only |

Unless stated otherwise, response objects may carry extra properties the client doesn't read —
only *duplicate* property names on the same JSON object are rejected.

### 1. Register definitions

```http
POST /v1/client/definitions HTTP/1.1
Authorization: Bearer <api-key>
Content-Type: application/json

{"definitions":[{"key":"Shop.Checkout.Enabled","kind":"Boolean","defaultValue":false}]}
```

- `kind` is exactly `"Boolean"` or `"String"`.
- `defaultValue` is a JSON boolean for `Boolean`, a JSON string for `String`.
- Success: `204`, no body required.
- The client never reads the response body of a `204` — you're free to send limits, counts, or
  nothing at all.

### 2. Read values (conditional GET)

This is the one endpoint the client parses strictly — `FeatureSnapshotReader` validates the
response byte-for-byte and discards anything that doesn't match, rather than degrading gracefully.

```http
GET /v1/client/values HTTP/1.1
Authorization: Bearer <api-key>
If-None-Match: W/"<environment-id>:<last-known-revision>"
```

(`If-None-Match` is omitted on the first request for a given process.)

**200 — snapshot:**

```http
HTTP/1.1 200 OK
ETag: W/"<environment-id>:<revision>"
Content-Type: application/json

{"revision":4,"values":[{"key":"Shop.Checkout.Enabled","kind":"Boolean","value":true}]}
```

Requirements the client enforces exactly:

- Status must be `200` and `Content-Type` must be exactly `application/json`.
- Exactly one `ETag` header, a valid HTTP entity tag (weak, `W/"..."`, is fine and is what
  `TinyFlags.Server` sends).
- The tag's quoted value must be `"<environment-id, GUID D-format>:<revision>"`, and that revision
  must equal the body's `revision` — a mismatch is treated as a protocol error, not a value.
- `revision`: required, a non-negative JSON integer.
- `values`: required JSON array. Each entry needs a non-blank string `key`, a `kind` of exactly
  `"Boolean"` or `"String"`, and a `value` matching that kind. Duplicate `key`s in the array are
  rejected.
- Body size is capped by the client's `MaxSnapshotBytes` (default 8 MiB), checked against both
  `Content-Length` and bytes actually read.

**304 — unchanged:**

```http
HTTP/1.1 304 Not Modified
ETag: W/"<environment-id>:<revision>"
```

Only meaningful in response to an `If-None-Match` for that exact revision — the client checks the
returned tag's revision against the snapshot it already holds. A `304` with no `ETag`, or one from
a different environment, is a protocol error, not "no changes."

`Cache-Control: no-store` is recommended (browsers/proxies shouldn't cache a per-key response) but
not checked by the client.

### 3. Update values (write)

Nothing in the SDK calls this — generated flags never write, only read. It exists purely for
external tooling; `TinyFlags.Server`'s own admin dashboard uses it to let a human flip a flag. A
custom server can implement it however it likes, give it a completely different shape, or omit it
entirely: nothing about the read path depends on it. Shown here for completeness, describing what
`TinyFlags.Server` itself does:

```http
PATCH /v1/client/values HTTP/1.1
Authorization: Bearer <api-key>
Content-Type: application/json

{"values":[{"key":"Shop.Checkout.Enabled","value":true}]}
```

There's no `kind` field — the kind is inferred from the JSON literal (`true`/`false` → Boolean, a
string → String) and checked against the key's already-registered kind.

| Outcome (`TinyFlags.Server`'s behavior) | Status |
| --- | --- |
| Applied | `204` |
| Unknown key(s) | `400`, body includes `unknownKeys` |
| Value kind mismatch | `400`, body includes `kindMismatchKeys` |
| Malformed batch (bad shape, string too long, duplicate key) | `400` |
| Missing/invalid credential | `401` |
| Valid credential, no write grant | `403` |
| Body too large | `413` |
| Non-JSON content | `415` |

## How the client interprets error statuses

`TinyFlagsApiClient` maps both registration and values-read responses through the same table —
this is what a permanent, non-retried failure looks like from your server's response code alone:

| Status | Client outcome |
| --- | --- |
| `401` | Credentials rejected |
| `403` | Access denied |
| `409` | Definitions conflict |
| Any other `4xx` | Request rejected |
| Anything else (unclassified, or a malformed body) | Invalid response |

`408`, `429`, and every `5xx` never reach that table. The client retries them first, with
exponential backoff (`RetryDelay` up to `MaxRetryDelay`, jittered), honoring a `Retry-After` header
on `429`/`503` if you send one. Retries continue indefinitely — bounded only by the caller's own
cancellation. A server that's persistently down doesn't "fail" the client; it just keeps the host
running on its last known values until the server recovers.

## What must match exactly vs. what's yours to design

**Fixed by the client:**
- The three routes and methods, relative to your `Endpoint`
- JSON property names and casing (`key`, `kind`, `value`, `defaultValue`, `revision`, `values`)
- The status codes listed above, in the situations described
- The `ETag` format and its revision/body consistency rule on `GET`

**Entirely up to you:**
- Storage, transactions, concurrency strategy
- What the Bearer token actually is and how you validate it
- Any limits — batch size, string length, key length — the client has no hard-coded expectations
  about limits, only about response shape
- Rate limiting policy (the client just honors `Retry-After` if present)
- Whether you implement the write endpoint at all

## See also

- [Registration](registration.md) — the SDK-side registration worker, and `TinyFlags.Server`'s
  specific limits and concurrency behavior
- [Value Synchronization](value-synchronization.md) — the SDK-side polling worker and its failure
  handling
- [TinyFlags.Server](https://github.com/george2006/TinyFlags.Server) — deploying the reference
  implementation itself
