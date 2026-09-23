# Architecture overview

TinyFlags consists of:

- a small runtime core (`FeatureValues`, catalog composition, DI glue, and the transport
  contracts every server integration implements against)
- a source generator that discovers, analyzes, validates and emits typed access classes
- reference transport implementations — `TinyFlags.Http` (registration + polling) and
  `TinyFlags.Grpc` (registration + real-time push) — each a complete, independent way to talk to
  a server; neither depends on the other existing
- `TinyFlags.Server`, a PostgreSQL-backed reference server speaking both wire protocols

No transport is privileged. `TinyFlags.Http` isn't "the SDK's built-in client" with `TinyFlags.Grpc`
bolted on beside it — both are equal implementations of the same core contracts, and the whole
point of the split is that a third one (yours) is exactly as legitimate as either. See
[Building a Transport](building-a-transport.md) if that's what you're here for.

```text
src/TinyFlags                  Marker, local values, catalog composition, transport contracts (net8.0)
src/TinyFlags.Http             Reference transport: HTTP registration + polling (net8.0)
src/TinyFlags.Grpc             Reference transport: gRPC registration + real-time push (net8.0)
src/TinyFlags.SourceGen        Incremental generator (netstandard2.0)
```

## Generator pipeline

Discovery -> Analysis -> Validation -> Generation.

- **Discovery** is the initial syntax filter: which classes are worth a closer semantic look.
- **Analysis** resolves the `IFeatureProvider` marker, combines partial members, and extracts flag
  information. Roslyn symbols never leave this phase.
- **Validation** and every model after it are plain data — scalar values, source positions, and
  issues — with no Roslyn dependency.
- **Generation** receives validated definitions and returns source text, also with no Roslyn
  dependency. It splits into `Generation/Planning` (what to emit) and `Generation/Emission`
  (turning a plan into C# source).

Models compare by value, including their collections, so an edit that does not change the
extracted meaning reuses downstream validation and generation, even if source positions moved.
Partial declarations are combined into one provider and contribute once.

Each assembly also gets a generated module initializer that registers its local catalog with
`TinyFlagsBootstrap` on load. The root application composes every initialized assembly's
contribution through `TinyFlagsBootstrap.GetDefinitions()`. Equivalent keys with the same kind and
default appear once; conflicting kinds or defaults throw during composition. The bootstrap never
scans or loads assemblies itself — only initialized ones contribute.

## Supported declarations

- Top-level concrete classes, public or internal, without generic parameters or class inheritance,
  implementing the `IFeatureProvider` marker (no methods, no implementation required).
- Partial classes are combined into a single provider.
- Every declared property must be public, instance, read-only, non-indexed, and either `bool` or
  non-nullable `string`. Setters, including private setters and `init`, are rejected.
- Defaults must be non-null compile-time constants: literals, `const` references, `nameof`, and
  constant expressions, resolved through Roslyn's semantic analysis.
- Supported bodies: `=> constant`, `{ get => constant; }`, `{ get { return constant; } }`, and
  `{ get; } = constant`. Getters with additional statements are rejected.
- Records, structs, abstract, nested, generic, and file-local providers are not supported.
  Helpers and constant fields do not become flags.

Neither constructors nor getters are ever executed to discover defaults. Classes without the
marker are ignored. A provider with an invalid property produces a diagnostic and contributes no
partial definition. An otherwise-empty provider contributes no flags. Flag identity is the fully
qualified provider name plus the property name — renaming either changes identity. Properties
within a provider are ordered ordinally, but provider arrival order across a compilation is not a
catalog ordering contract.

See [Diagnostics](diagnostics.md) for every `TFG0xx` code with examples.

Because identity is just this string, two independent services that declare the same namespace,
class, and property name end up sharing one flag with no configuration at all. See
[Sharing a Flag Across Services](multi-service-flags.md) for why that works and how to do it on
purpose.

## Runtime components

Local-only (always present):

| Component | Responsibility |
| --- | --- |
| `FeatureValues` | Local snapshot store: typed reads, default fallback, atomic replacement |
| Generated `XxxFeatureFlags` | Typed getters backed by `FeatureValues`, one per provider |
| `TinyFlagsBootstrap` | Cross-assembly catalog composition |

Transport contracts, in core `TinyFlags` (`Abstractions/`) — implemented by `TinyFlags.Http`,
`TinyFlags.Grpc`, or your own package:

| Component | Responsibility |
| --- | --- |
| `IFeatureDefinitionsTransport` | Sends the composed catalog to a server. One-shot request/response — no pull or push variant, since there's nothing to pull or push about registration |
| `IFeatureValuesTransport` | Pull: fetch current values, conditionally on a cursor already held locally |
| `IFeatureValuesSubscription` | Push: stream value changes as they happen, instead of being polled for them |
| `FeatureValuesCursor` / `FeatureValuesResult` | Identity/staleness and the payload, shared by both values contracts |
| `TinyFlagsRegistrationWorker` | Drains `IFeatureDefinitionsTransport` once, after startup |
| `TinyFlagsSynchronizationWorker` | Drains `IFeatureValuesTransport` on a recurring interval — the only writer to `FeatureValues` when using a pull transport |
| `TinyFlagsValuesWatchWorker` | Drains `IFeatureValuesSubscription`'s stream — the only writer to `FeatureValues` when using a push transport |

Each transport picks which of these it wires up via its own `Use...Transport(...)` extension on
`TinyFlagsOptions`, called from `AddTinyFlags`'s configure callback — see
[Building a Transport](building-a-transport.md) for exactly how that plugs in.

Server (`TinyFlags.Server` — a separate, privately-hosted reference implementation; this is its
conceptual shape, not something you need access to it to understand):

| Component | Responsibility |
| --- | --- |
| `Features/RegisterDefinitions` | TinyDispatcher command that persists a registration batch — dispatched by both the REST and gRPC registration endpoints |
| `Features/GetFeatureValues` | TinyDispatcher query that reads one consistent revision + values snapshot — dispatched by both the REST endpoint and the gRPC `Watch` service |
| `Authentication/ClientApiKey` | Hashed, scoped, per-environment credentials with explicit grants, checked identically for REST and gRPC (gRPC metadata surfaces through the same ASP.NET Core auth middleware as HTTP headers) |
| `Persistence` | EF Core + PostgreSQL: projects, environments, definitions, `ValuesRevision` |

## Ownership boundaries

- Contributing assemblies discover and validate their own providers at build time; nothing runs at
  runtime to find them.
- The host is the sole composer of the final catalog (`TinyFlagsBootstrap.GetDefinitions()`).
- Registration and value sync are independent regardless of transport: registration never mutates
  `FeatureValues`, and no values worker ever waits for registration to complete or succeed.
- The server never originates a flag. It stores and serves values for definitions a client already
  registered; see [Registration](registration.md) for the exact contract.
- A transport only ever knows a base address, a Bearer token, and the contracts above — no
  transport, including the ones we ship, has special access the interfaces don't expose. See
  [Server Protocol (HTTP)](protocol.md) or [Server Protocol (gRPC)](grpc-protocol.md) if you're
  implementing your own server; see [Building a Transport](building-a-transport.md) if you're
  implementing the client side instead.
