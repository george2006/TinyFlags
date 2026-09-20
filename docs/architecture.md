# Architecture overview

TinyFlags consists of:

- a small runtime core (`FeatureValues`, catalog composition, DI glue)
- a source generator that discovers, analyzes, validates and emits typed access classes
- an SDK that talks to the server (registration + value synchronization)
- `TinyFlags.Server`, a PostgreSQL-backed HTTP service that stores definitions and serves values

```text
src/TinyFlags                  Marker, local values and catalog composition (net8.0)
src/TinyFlags.SourceGen        Incremental generator (netstandard2.0)
src/TinyFlags.Server           Registration/values API, PostgreSQL persistence (net10.0)
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
contribution through `TinyFlagsBootstrap.GetDefinitions()`; equivalent keys with the same kind and
default appear once, and conflicting kinds or defaults throw during composition. The bootstrap
never scans or loads assemblies itself — only initialized ones contribute.

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
partial definition; an otherwise-empty provider contributes no flags. Flag identity is the fully
qualified provider name plus the property name — renaming either changes identity. Properties
within a provider are ordered ordinally; provider arrival order across a compilation is not a
catalog ordering contract.

See [Diagnostics](diagnostics.md) for every `TFG0xx` code with examples.

## Runtime components

Local-only (always present):

| Component | Responsibility |
| --- | --- |
| `FeatureValues` | Local snapshot store: typed reads, default fallback, atomic replacement |
| Generated `XxxFeatureFlags` | Typed getters backed by `FeatureValues`, one per provider |
| `TinyFlagsBootstrap` | Cross-assembly catalog composition |

Added when `AddTinyFlags` is configured with server options:

| Component | Responsibility |
| --- | --- |
| `TinyFlagsApiClient` | Translates HTTP into application results; owns the retry policy and snapshot reader |
| `TinyFlagsRetryPolicy` | Attempts, classification, backoff and cancellation for one HTTP operation |
| `FeatureSnapshotReader` | Bounded body reading, JSON/ETag validation, snapshot construction |
| `TinyFlagsRegistrationWorker` | Sends the composed catalog to the server after startup |
| `TinyFlagsSynchronizationWorker` | Fetches and republishes values after startup, then on a recurring interval — the SDK's only writer to `FeatureValues` |

Server (`TinyFlags.Server`, see [Running the Server](server.md)):

| Component | Responsibility |
| --- | --- |
| `Features/RegisterDefinitions` | TinyDispatcher command that persists a registration batch |
| `Features/GetFeatureValues` | TinyDispatcher query that reads one consistent revision + values snapshot |
| `Authentication/ClientApiKey` | Hashed, scoped, per-environment credentials with explicit grants |
| `Persistence` | EF Core + PostgreSQL: projects, environments, definitions, `ValuesRevision` |

## Ownership boundaries

- Contributing assemblies discover and validate their own providers at build time; nothing runs at
  runtime to find them.
- The host is the sole composer of the final catalog (`TinyFlagsBootstrap.GetDefinitions()`).
- The SDK's registration and synchronization workers are independent: registration never mutates
  `FeatureValues`, and synchronization never waits for registration to complete or succeed.
- The server never originates a flag. It stores and serves values for definitions the SDK already
  registered; see [Registration](registration.md) for the exact contract.
