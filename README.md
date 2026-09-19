# TinyFlags

Typed feature flag declarations for .NET.

## Current behavior

The generator discovers feature providers, reads their flag definitions, and reports unsupported
declarations at compile time. It generates typed access classes backed by `FeatureValues`.
Each assembly also gets a local registration catalog and contributes it automatically when
its module initializes. The root application can compose these definitions through
`TinyFlagsBootstrap`. `AddTinyFlags()` registers generated access classes from initialized
assemblies using a shared local store. The runtime and generator pack into one NuGet package,
verified with a separate local consumer. Configured clients independently register definitions and
load an initial server snapshot after host startup, retrying transient failures. Existing generated
instances observe published values through the shared store. No package is published yet.
The server exposes authenticated definition registration and conditional snapshot reads over HTTP.

```csharp
using TinyFlags;

namespace MyApp;

public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
```

The analysis produces these definitions:

| Key | Type | Default |
| --- | --- | --- |
| `MyApp.Checkout.NuevoCheckout` | Boolean | `false` |
| `MyApp.Checkout.TextoBoton` | String | `"Comprar"` |

`IFeatureProvider` is a marker, with no methods or implementation. Declaration getters describe
defaults; calling them directly still returns those defaults. The generated public sealed
class is `MyApp.CheckoutFeatureFlags`. Its constructor takes `FeatureValues`, and each getter
reads the current store with the declared default as fallback:

```csharp
var values = new FeatureValues();
var flags = new MyApp.CheckoutFeatureFlags(values);

bool enabled = flags.NuevoCheckout; // false
values.ReplaceSnapshot(new Dictionary<string, object>
{
    ["MyApp.Checkout.NuevoCheckout"] = true
});
enabled = flags.NuevoCheckout; // true, using the same generated instance
```

Generated classes do not implement the declaration marker or instantiate declaration classes.
Partial declarations produce one access class. Escaped C# identifiers and string literals are
supported. A generated class name must not conflict with an existing type, namespace, or one
of its feature properties; conflicts produce `TFG004` instead of a generated class.

## Registration definitions

`FeatureDefinition` is immutable metadata for the registration catalog:

```csharp
var enabled = FeatureDefinition.Boolean("MyApp.Checkout.NuevoCheckout", false);
var label = FeatureDefinition.String("MyApp.Checkout.TextoBoton", "Comprar");
```

Each definition exposes `Key`, `Kind` (`FeatureKind.Boolean` or `FeatureKind.String`), and
`DefaultValue`. The default is a boxed boolean or a non-null string, matching its kind. Typed
factories keep that pairing valid; keys must not be blank. Empty string defaults are supported.
Definitions describe declared defaults; live values are held in `FeatureValues`.
The generator exposes an internal catalog in each assembly:

```csharp
var definitions = TinyFlags.Generated.ThisAssemblyFeatureCatalog.Definitions;
```

`Definitions` is an immutable `IReadOnlyList<FeatureDefinition>`, sorted by key. It includes only
valid providers from that assembly; partial declarations contribute once. It is empty when
there are no valid flags. Reading it never executes declaration constructors or getters.
The generated module initializer registers that catalog with `TinyFlagsBootstrap`. The root
application composes the registered contributions:

```csharp
var definitions = TinyFlagsBootstrap.GetDefinitions();
```

The result is an immutable snapshot ordered ordinally by key, including contributions from
initialized library modules and the host. Equivalent keys with the same kind and default appear
once; conflicting kinds or defaults throw `InvalidOperationException` during composition.
Keys are case-sensitive. Later contributions appear in subsequent snapshots without changing
earlier results.

Generated code calls `AddContribution` with definitions and a service registration callback;
applications use `AddTinyFlags()` and `GetDefinitions()`. Registration copies the supplied collection, and the
first registration per catalog type wins. Identically named catalog types from different
assemblies remain separate contributors. Registration and composition are thread-safe.

The bootstrap does not scan, load, or initialize assemblies. Referencing a library without
using it does not guarantee its contribution has registered. Compose after the relevant
modules have initialized; automatic inclusion of unused references is not implemented.

## Dependency injection

`AddTinyFlags()` registers a singleton `FeatureValues` per container and applies registered
assembly callbacks. The generator emits typed singleton factories for each valid access class,
including empty providers. Existing store and access-class registrations are preserved:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TinyFlags;

var services = new ServiceCollection();
services.AddTinyFlags();
using var provider = services.BuildServiceProvider();
var flags = provider.GetRequiredService<MyApp.CheckoutFeatureFlags>();
```

The runtime supports `AddContribution(Type, IReadOnlyList<FeatureDefinition>,
Action<IServiceCollection>)` for assembly registration code. The first registration per type
keeps both its definitions and its callback, including metadata-only registrations. Generated
factories resolve FeatureValues from the container. Scopes share the same store and access
instances; separate containers have independent instances unless explicitly supplied by callers.

Each AddTinyFlags call applies a snapshot of registered callbacks outside the bootstrap lock.
Callbacks must support repeated application, for example through TryAddSingleton. A failed
callback propagates its exception; already-added services remain. Later contributions can be
applied by calling AddTinyFlags again before building the container. Registration does not
modify an already-built service provider. Configure each service collection sequentially.

Generated registration performs no network I/O. The configured overload below enables background
server registration without making application startup wait for the server.

## Local values

`FeatureValues` reads boolean and string values from a shared local snapshot:

```csharp
var values = new FeatureValues();
values.ReplaceSnapshot(new Dictionary<string, object>
{
    ["MyApp.Checkout.NuevoCheckout"] = true,
    ["MyApp.Checkout.TextoBoton"] = "Buy"
});

bool enabled = values.GetBoolean("MyApp.Checkout.NuevoCheckout", defaultValue: false);
string label = values.GetString("MyApp.Checkout.TextoBoton", defaultValue: "Comprar");
```

Missing keys and mismatched types use the supplied default. Keys are case-sensitive. A snapshot
replacement copies and validates all entries before publishing the new dictionary atomically;
invalid values leave the old snapshot intact. Later changes to the supplied dictionary have no
effect. Flags absent from a replacement return to their defaults; an empty snapshot clears all
overrides. Values must be booleans or non-null strings, and keys must not be blank.

Each read observes one complete snapshot. Multiple reads can observe different versions when
an update occurs between them. Configured hosts load an initial revisioned server snapshot after startup;
recurring refresh is the next synchronization slice.

## Supported declarations

- Top-level concrete classes, public or internal, without generic parameters or class inheritance.
- Partial classes are combined into a single provider.
- Every declared property must be public, instance, read-only, non-indexed, and either `bool`
  or non-nullable `string`. Setters, including private setters and `init`, are rejected.
- Defaults must be non-null compile-time constants. Literals, `const` references, `nameof`,
  and constant expressions are supported through Roslyn's semantic analysis.
- Supported bodies: `=> constant`, `{ get => constant; }`, `{ get { return constant; } }`,
  and `{ get; } = constant`. Getters with additional statements are rejected.
- Records, structs, abstract, nested, generic, and file-local providers are not supported in
  this first slice. Helpers and constant fields do not become flags.

Neither constructors nor getters are executed to discover defaults. Classes without the
TinyFlags marker are ignored. A provider with an invalid property produces diagnostics and
does not contribute a partial definition. Empty providers contribute no flags.

Flag identity is the fully qualified provider name plus the property name. Renaming either
changes the identity. Properties with the same name in different providers remain distinct.
Properties within each provider are ordered ordinally. Providers travel independently through
the pipeline; their arrival order is not a catalog ordering contract.

## Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| `TFG001` | Error | Unsupported provider shape |
| `TFG002` | Error | Unsupported property shape or type |
| `TFG003` | Error | Missing, null, or nonconstant default |
| `TFG004` | Error | Generated class name conflicts |
| `TFG005` | Error | Generated catalog name conflicts |

## Components

```text
src/TinyFlags                  Marker, local values and catalog composition (net8.0)
src/TinyFlags.SourceGen        Incremental generator (netstandard2.0)
tests/TinyFlags.Tests
tests/TinyFlags.SourceGen.Tests
```

The implemented flow is discovery -> analysis -> validation -> generation.
`Discovery` performs the initial syntax filter. `Analysis` resolves the marker, combines partial
members, and extracts flag information. The generator entry point connects these phases through
Roslyn's syntax predicate and semantic transform.
Roslyn symbols stay inside `Analysis`. Models and validation have no Roslyn dependencies;
source positions and issues are plain data. The generator entry point and diagnostic reporter
adapt these results to Roslyn. `Generation` receives validated definitions and returns source
text, with no Roslyn dependency:

```text
Generation/
  FeatureGeneration.cs
  Planning/
    FeatureAccessPlanner.cs
    FeatureAccessPlan.cs
    FeatureCatalogPlanner.cs
    FeatureCatalogPlan.cs
  Emission/
    FeatureAccessEmitter.cs
    FeatureCatalogEmitter.cs
```

The incremental pipeline analyzes and validates providers individually. Models compare by
value, including their collections, so equivalent results reuse downstream work. Partial
declarations contribute one provider. Roslyn may repeat semantic analysis after an edit;
validation is reused when the extracted model is unchanged. Unchanged validated definitions
reuse generation, even if their source positions moved. Diagnostic reporting binds
locations to the current compilation.

Tests use real Roslyn compilations and the actual marker assembly. They verify extracted
definitions, constant values, marker identity, diagnostic IDs, and diagnostic locations.
Generated access tests compile and load consumer assemblies, then execute typed getters against
real `FeatureValues` snapshots. They check default fallback, updates, escaping, and naming conflicts.
Multi-assembly tests compile separate libraries and a host, then execute module initializers
and root composition. They cover local catalog isolation, transitive calls, load order,
duplicate registration, equivalent definitions, and conflicting defaults/types. Each scenario
uses an isolated runtime instance so static contributions cannot leak between tests.
Incremental tests reuse the same driver across edits and inspect tracked step results to verify
cache reuse and invalidation, including external constants and partial declarations.
The generator tests invoke Roslyn directly. A separate package verification script tests
automatic generator inclusion through ordinary PackageReference consumers.

The full solution also includes server integration tests and requires .NET 10 SDK, .NET 8 and
ASP.NET Core 8 runtimes, PowerShell (Windows PowerShell on Windows or pwsh elsewhere), NuGet access,
and a running Docker engine with Linux containers. The packaged integration test builds an isolated
consumer through the package script as part of the suite:

```shell
dotnet test TinyFlags.slnx -warnaserror
```

## Server registration foundation

`TinyFlags.Server` contains project/environment persistence and the TinyDispatcher registration
command in `Features/RegisterDefinitions`. DefinitionInput converts incoming values;
RegistrationBatch validates duplicates and selects missing definitions; the handler reads and
saves through DbContext. Same-kind replay preserves existing defaults and creation timestamps.
The server targets net10.0; the NuGet client continues to target net8.0 so applications can use
the same package before and after upgrading from .NET 8 to .NET 10.

Registered keys are case-sensitive, scoped by environment, and limited to 400 characters.
Boolean/string default columns are protected by a database check constraint. Invalid batches
are rejected before writing. Each nonempty registration locks its environment row within a
Read Committed transaction before reading definitions. Concurrent registrations for that
environment run in order; other environments use independent locks. Kind conflicts raise
`FeatureKindConflictException` with all conflicting keys and leave the entire batch unwritten.
Each environment now has a ValuesRevision. A batch inserting flags advances it once, atomically
with those inserts; empty batches, replays, conflicts and failed writes do not advance it. Existing
environments with definitions migrate to revision one; empty ones start at zero. This establishes
the revision for the values endpoint and SDK synchronization worker.
The new GetFeatureValues vertical slice reads revision and typed values through TinyDispatcher
in one Repeatable Read transaction. Matching revisions return an unchanged result without reading
the flags; initial empty environments return a full empty snapshot. The SDK loads one initial snapshot
after host startup; recurring synchronization is not implemented yet.
`POST /v1/client/definitions` exposes registration. The configured SDK registers in the background
after host startup and retries transient failures until success, a permanent failure or shutdown.

Client authentication is implemented in `Authentication/`. `ClientApiKey.Issue` returns a random
256-bit secret separately from the entity; PostgreSQL stores only its SHA-256 hash. Requests use
`Authorization: Bearer <client-api-key>`. Each key identifies one environment and its project,
with an explicit registration grant. `ClientRegistration` requires the `ClientApiKey` scheme and
the `definitions:register` permission, independent of future dashboard identities and roles.
Revocation is checked against the database on each new request; requests already authenticated
may finish. Multiple keys can coexist during replacement. Key-management endpoints remain future work.

`GET /v1/client/values` returns the authenticated environment's complete snapshot:

```json
{"revision":1,"values":[{"key":"Shop.Checkout.Enabled","kind":"Boolean","value":false}]}
```

Responses include `ETag: W/"<environment-id>:1"` and `Cache-Control: no-store`. Send that tag in
`If-None-Match` to receive 304 with no body when unchanged. Weak/strong tags, lists and `*` are
supported; another environment's tag cannot match. Malformed conditional headers return 400.
Authentication and authorization run before conditional matching, including on 304 responses.

The independent `ClientValues` policy requires the `values:read` permission from a client API key.
The migration grants existing keys read access; new keys default to read access and can explicitly
disable it with `ClientApiKey.Issue(..., canReadValues: false)`. Registration permissions are unchanged.
`Request` owns conditional-header interpretation and query creation; `FeatureValuesETag` owns
tag parsing and formatting. These concrete types belong to the GetFeatureValues vertical slice.

The registration endpoint accepts UTF-8 JSON with a `definitions` array:

```json
{
  "definitions": [
    { "key": "Shop.Checkout.Enabled", "kind": "Boolean", "defaultValue": false }
  ]
}
```

The API key supplies the environment; unknown envelope fields such as `environmentId` are
rejected. Limits are 1 MiB of body bytes (including requests without Content-Length), 1,000
definitions, 400 characters per key, and 4,096 characters per string default. An empty list
succeeds. Registration returns 204 after commit; invalid input returns 400, authentication 401,
insufficient permission 403, kind conflict 409 with `conflictingKeys`, excessive body/batch size
413, and non-JSON content 415. Transient database failures return 503, including during key
authentication. Unexpected server errors return 500 with no database details in the response.

Run just the server integration tests:

```shell
dotnet test tests/TinyFlags.Server.IntegrationTests -warnaserror
```

Testcontainers starts a digest-pinned PostgreSQL 16 container with an allocated port. Each test
gets a separate database initialized through actual migrations. Tests cover round-trips,
migration reapplication, uniqueness, foreign keys, restricted deletion and database isolation.
Registration tests exercise real TinyDispatcher dispatch, typed defaults, replay, scope isolation,
input validation, duplicate handling and the database kind/default constraint. Concurrency tests
use independent service providers and observe actual PostgreSQL lock waits before releasing
competing registrations. They cover identical and incompatible batches, independent environments,
cancellation and registration after a conflict. Additional cases cover overlapping batches with
different defaults and recovery after an uncommitted writer fails following insertion.
Authentication tests exercise real ASP.NET authentication and policy evaluation with PostgreSQL:
scope, permissions, malformed/invalid/revoked keys, replacement keys, dashboard identity isolation,
hash-only storage and database constraints. HTTP tests run the actual application using
WebApplicationFactory, changing only configuration. They cover the full auth/dispatcher/database
path, malformed and oversized requests, scope isolation, cancellation, and two hosts contending
on the same database. Outage tests stop their own container or terminate only a test request's
database connection; unexpected database errors remain 500.
Docker being unavailable fails the run; there is no in-memory fallback or automatic skip.

Test setup uses three concrete responsibilities: PostgreSqlFixture owns Docker and isolated
migrated databases; DatabaseSeed persists routine project/environment/client setup; and
TinyFlagsServerFactory hosts the application and creates authenticated clients:

```csharp
var options = await database.CreateDatabaseAsync();
var seed = new DatabaseSeed(options);
var credential = await seed.CreateClientAsync(canRegisterDefinitions: true);
await using var server = new TinyFlagsServerFactory(options);
using var client = server.CreateAuthenticatedClient(credential.Secret);
```

Each seed call creates a fresh project and environment within that test's database. Tests keep
special relationships, revocation, concurrency controls, requests and assertions explicit.
Handler and authentication tests reuse DatabaseSeed without needing an HTTP host.

For manual development, start the separate compose database and apply migrations explicitly:

```shell
docker compose up -d --wait
dotnet tool restore
dotnet ef database update --project src/TinyFlags.Server -- --environment Development
dotnet run --project src/TinyFlags.Server -- --environment Development --seed-development
dotnet run --project src/TinyFlags.Server -- --environment Development
```

The compose database listens on localhost:54324, with local development credentials matching
`appsettings.Development.json`. Its named volume persists across normal container stops.
Integration tests use their own containers and never reset this database. Stop the development
container with `docker compose down`; that preserves its volume. Outside Development, provide
`ConnectionStrings__TinyFlags`. Server startup does not run migrations.

The explicit `--seed-development` command creates a new local project, environment and authorized
key, prints the newly issued secret once, and exits. It requires the Development environment and
already-applied migrations. Repeating it creates a separate local project/key. Use the printed
secret as the Bearer credential when calling the endpoint; ordinary startup does not seed data.

## SDK API client

The internal `TinyFlagsApiClient` coordinates HTTP access and exposes application results. It snapshots validated
`TinyFlagsClientOptions`: Endpoint, ApiKey and RequestTimeout (30 seconds by default).
Endpoint is the server base URI, including any path prefix; a trailing slash is optional.
HTTPS is required except for HTTP loopback development. URI credentials, query and fragment,
blank/invalid credentials and invalid timeouts are rejected before sending anything.

`RegisterDefinitionsAsync` copies the catalog once and sends typed Boolean/String defaults to
`v1/client/definitions`. It completes after successful registration or throws TinyFlagsClientException
with a classified failure: rejected credentials, denied access, conflicting definitions, rejected
request or invalid response. Caller cancellation propagates. HTTP messages and response bodies stay
behind the client boundary; remote error text is not included in the classified exception.

DI creates a concrete FeatureSnapshotReader with the configured size limit and injects it into the
API client. The registration worker receives the client. The DI-owned client owns its HttpClient;
an explicitly supplied HttpClient remains caller-owned. Shared headers and timeout are not mutated.

The parameterless AddTinyFlags() remains local-only. To register definitions from a .NET host:

```csharp
builder.Services.AddTinyFlags(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]!);
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"];
});
```

Configuration is validated and copied during setup. Repeating equivalent configuration registers
one instance of each worker; conflicting configuration is rejected. After ApplicationStarted, the registration worker composes
the initialized assemblies' catalog and sends one request at a time. DI setup and flag getters never contact
the server. Shutdown cancels the startup wait or request; redirects are not followed. Failures are
logged without stopping the host. The independent synchronization worker loads the first snapshot
and retains local values if reading fails. Native .NET 8 host tests verify startup and shutdown; integration tests exercise registration
against the actual registration server, authentication, dispatcher and PostgreSQL over Kestrel.

The client uses TinyFlagsRetryPolicy for network failures, request timeouts, HTTP 408/429 and 5xx responses. Other
responses stop registration; 204 completes it. RetryDelay defaults to 1 second and MaxRetryDelay
to 30 seconds. The exponential backoff uses jitter between half and the full capped delay, with
a minimum of 1 millisecond. Both settings are validated and included in configuration snapshots.
Valid Retry-After values on 429/503 can extend the wait beyond the normal cap; malformed or expired
values retain ordinary backoff. Long waits remain cancellable. The same catalog is retried, with
no fixed attempt limit, and responses are disposed before waiting. The concrete TinyFlagsRetryPolicy
owns attempts, classification, delays and cancellation. It disposes every response, including the
final response after the client's reader translates it. The worker only awaits registration and
handles a classified failure. There is no Polly dependency.

## SDK snapshot fetch

`TinyFlagsApiClient.GetValuesAsync` accepts the local catalog and an optional accepted FeatureSnapshot.
It returns FeatureValuesResult: an updated immutable snapshot or IsUnchanged. It sends the conditional
header internally, validates unchanged responses against the accepted snapshot, ignores equal/older
revisions, and rejects an unexpected environment change. The caller never interprets status codes
or ETags. Transient failures use the same retry policy as registration; this operation does not publish values.

`MaxSnapshotBytes` defaults to 8 MiB and must be positive. The fetch checks both declared content
length and bytes actually read, including chunked responses. One request deadline covers headers,
the complete successful body and snapshot parsing; a shorter HttpClient timeout also applies to
the body. Cancellation or validation failure disposes the response. Interrupted body reads become
transport failures and are retried; malformed or oversized payloads become an InvalidResponse
failure rather than an empty or partially valid snapshot. Non-200 bodies are not buffered.

The constructor-injected `FeatureSnapshotReader` owns bounded body reading and validates the JSON shape,
nonnegative revision, matching environment/revision
ETag, unique nonblank keys, Boolean/String values and agreement with known catalog kinds. Valid
keys from other assemblies are accepted; absent keys remain absent so publication can retain the
existing default-fallback behavior. The snapshot owns an immutable dictionary independent of the
HTTP response. FeatureSnapshot itself contains only data and internal conditional-request metadata.
The reader does not impose registration's per-batch flag-count limit.

`TinyFlagsSynchronizationWorker` waits for ApplicationStarted and independently fetches one initial
snapshot. It publishes the complete values through the existing singleton's ReplaceSnapshot method;
already-created generated flag instances see the update on their next read. Empty revision-zero
snapshots clear previous values and restore declared defaults. The parameterless AddTinyFlags remains
local-only and registers neither worker.

Registration can be blocked or denied while reading succeeds. Transient read failures retry inside
the client; permanent or malformed initial responses end this read attempt without changing values
or stopping the host. Shutdown cancels the startup wait, download or retry delay. This worker is the
only SDK writer to FeatureValues. Recurring polling and later publication are the next slice.

## Local package verification

Pack the runtime and generator together:

```shell
dotnet pack src/TinyFlags/TinyFlags.csproj -c Release -o artifacts/packages
```

The package contains the net8.0 runtime, the netstandard2.0 generator under
`analyzers/dotnet/cs`, and this README. Consumers reference TinyFlags in each project declaring
flags; no separate analyzer reference is needed. The runtime directly depends on DI abstractions
and Hosting abstractions. The generator's Roslyn dependencies are supplied by the compiler host.

Run the package smoke test from PowerShell:

```powershell
./tests/TinyFlags.PackageTests/Verify-Package.ps1
```

It packs a unique local version, restores a separate host/library pair into a fresh package
cache, builds and runs it, then verifies that an invalid declaration produces TFG002. The
consumer checks defaults, automatic DI across initialized assemblies, shared updates, and root
catalog composition. The generator DLL must not be copied to the application's runtime output.
Artifacts and the expected failing build log remain under `artifacts/package-tests`.
The script needs NuGet access for Microsoft.Extensions dependencies and does not publish packages.

The server integration suite also launches this packed net8.0 consumer as a separate process
against the real API and Docker PostgreSQL. It verifies startup with registration blocked at the
database, multi-assembly definitions, and redeployment with changed defaults plus a new flag.
Existing definitions retain their defaults and timestamps. Package, build and consumer process
logs remain together under the test's unique artifacts directory. The smoke script's optional
`-RunDirectory` parameter selects that directory for integration-test orchestration.

See [the working agreement](WORKING-AGREEMENT.md) and [the slice plan](PLAN.md).
