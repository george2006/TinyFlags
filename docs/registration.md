# Registration

Applications declare their flags in code. Registration is how the SDK tells a `TinyFlags.Server`
environment which flags exist, so the server can store values for them. It never lets the server
invent a flag.

## Catalog composition

`FeatureDefinition` is immutable metadata: `Key`, `Kind` (`FeatureKind.Boolean` or
`FeatureKind.String`), and a boxed/non-null `DefaultValue` matching that kind.

```csharp
var enabled = FeatureDefinition.Boolean("MyApp.Checkout.NuevoCheckout", false);
var label = FeatureDefinition.String("MyApp.Checkout.TextoBoton", "Comprar");
```

The generator exposes an internal, sorted, deduplicated catalog per assembly:
`TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions`. It's populated without executing any
declaration constructor or getter.

A generated module initializer registers that catalog with `TinyFlagsBootstrap`. The host composes
every initialized assembly's contribution through `TinyFlagsBootstrap.GetDefinitions()`.

See [Architecture](architecture.md#generator-pipeline) for how composition and conflict detection
work.

## What the registration worker does

`TinyFlagsRegistrationWorker` runs once per host, after `ApplicationStarted`:

1. Composes the catalog from every initialized assembly.
2. Calls `TinyFlagsApiClient.RegisterDefinitionsAsync`.

**Transient failures** — network errors, timeouts, 408/429/5xx — retry with backoff through
`TinyFlagsRetryPolicy`, honoring `Retry-After` on 429/503.

**Permanent failures** — rejected credentials, denied access, a definitions conflict, a rejected
request — stop the worker. The host keeps running; local flag values stay whatever they already
were.

Registration never touches `FeatureValues` — only
[the synchronization worker](value-synchronization.md) does.

## HTTP contract

`POST /v1/client/definitions`, authenticated with `Authorization: Bearer <client-api-key>`, scoped
to the key's environment:

```json
{
  "definitions": [
    { "key": "Shop.Checkout.Enabled", "kind": "Boolean", "defaultValue": false }
  ]
}
```

The API key supplies the environment; an `environmentId` or any other unknown envelope field is
rejected. Limits: 1 MiB of body bytes (including requests without `Content-Length`), 1,000
definitions per batch, 400 characters per key, 4,096 characters per string default. An empty list
succeeds and changes nothing.

| Outcome | Status |
| --- | --- |
| Registered | 204, no body |
| Invalid input | 400 |
| Missing/invalid credential | 401 |
| Valid credential, no `definitions:register` grant | 403 |
| Kind conflict with an existing definition | 409, body includes `conflictingKeys` |
| Body or batch too large | 413 |
| Non-JSON content | 415 |
| Transient database failure (including during key authentication) | 503 |
| Unexpected server error | 500, no database details in the response |

Registering the same key with the same kind and default is a no-op replay: existing defaults and
creation timestamps are preserved. Registering a different kind or default for an existing key
raises a kind conflict for the whole batch — nothing in that batch is written, not just the
conflicting keys.

## Authentication and permissions

`ClientApiKey.Issue` returns a random 256-bit secret once. PostgreSQL stores only its SHA-256
hash — never the secret itself.

Each key identifies one environment and one project, with three independent grants:

- `definitions:register`
- `values:read` — see [Value Synchronization](value-synchronization.md)
- `values:write` — required by `PATCH /v1/client/values`, see the [Server Protocol](protocol.md)

`values:write` without `values:read` is rejected at issuance; there is no write-without-read
combination.

Revocation is checked on every new request — a request already authenticated is allowed to finish.
Multiple keys can coexist during rotation.

See [TinyFlags.Server's docs](https://github.com/george2006/TinyFlags.Server/blob/main/docs/server.md#issuing-keys)
to issue a key from the command line.

## Concurrency and consistency

Each nonempty registration locks its environment row inside a Read Committed transaction before
reading current definitions. Concurrent registrations for the same environment apply in order;
other environments use independent locks and are unaffected.

A batch that inserts one or more new flags advances the environment's `ValuesRevision` exactly
once, atomically with those inserts. Empty batches, pure replays, conflicts, and failed writes
never advance it — that revision is what
[value synchronization](value-synchronization.md) uses to know whether anything changed.
