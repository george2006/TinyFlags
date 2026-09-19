# TinyFlags

Typed feature flag declarations for .NET.

## Current behavior

The generator discovers feature providers, reads their flag definitions, and reports unsupported
declarations at compile time. It generates typed access classes backed by `FeatureValues`.
Each assembly also gets a local registration catalog and contributes it automatically when
its module initializes. The root application can compose these definitions through
`TinyFlagsBootstrap`. `AddTinyFlags()` registers generated access classes from initialized
assemblies using a shared local store. The runtime and generator pack into one NuGet package,
verified with a separate local consumer. Networking is not implemented; no package is published yet.

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

Generated registration performs no network I/O. Future server registration and synchronization
will run in a background worker without making application startup wait for the server.

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
an update occurs between them. Server revisions and synchronization are future work.

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

The full solution also includes server integration tests and requires .NET 10 SDK, .NET 8 runtime,
and a running Docker engine with Linux containers:

```shell
dotnet test TinyFlags.slnx -warnaserror
```

## Server registration foundation

`TinyFlags.Server` contains project/environment persistence and the TinyDispatcher registration
command in `Features/RegisterDefinitions`. DefinitionInput converts incoming values;
RegistrationBatch validates duplicates and selects missing definitions; the handler reads and
saves through DbContext. Same-kind replay preserves existing defaults and creation timestamps.
The server targets net10.0; the NuGet client continues to target net8.0.

Registered keys are case-sensitive, scoped by environment, and limited to 400 characters.
Boolean/string default columns are protected by a database check constraint. Invalid batches
are rejected before writing. Concurrency coordination and the dedicated conflict exception
are the next slice; racing initial registrations can currently fail on the unique database key.
No registration endpoint or background worker is exposed yet.

Run just the database integration tests:

```shell
dotnet test tests/TinyFlags.Server.IntegrationTests -warnaserror
```

Testcontainers starts a digest-pinned PostgreSQL 16 container with an allocated port. Each test
gets a separate database initialized through actual migrations. Tests cover round-trips,
migration reapplication, uniqueness, foreign keys, restricted deletion and database isolation.
Registration tests exercise real TinyDispatcher dispatch, typed defaults, replay, scope isolation,
input validation, duplicate handling and the database kind/default constraint.
Docker being unavailable fails the run; there is no in-memory fallback or automatic skip.

For manual development, start the separate compose database and apply migrations explicitly:

```shell
docker compose up -d --wait
dotnet tool restore
dotnet ef database update --project src/TinyFlags.Server -- --environment Development
dotnet run --project src/TinyFlags.Server -- --environment Development
```

The compose database listens on localhost:54324, with local development credentials matching
`appsettings.Development.json`. Its named volume persists across normal container stops.
Integration tests use their own containers and never reset this database. Stop the development
container with `docker compose down`; that preserves its volume. Outside Development, provide
`ConnectionStrings__TinyFlags`. Server startup does not run migrations or expose business routes yet.

## Local package verification

Pack the runtime and generator together:

```shell
dotnet pack src/TinyFlags/TinyFlags.csproj -c Release -o artifacts/packages
```

The package contains the net8.0 runtime, the netstandard2.0 generator under
`analyzers/dotnet/cs`, and this README. Consumers reference TinyFlags in each project declaring
flags; no separate analyzer reference is needed. DI abstractions are the runtime package's
only NuGet dependency. The generator's Roslyn dependencies are supplied by the compiler host.

Run the package smoke test from PowerShell:

```powershell
./tests/TinyFlags.PackageTests/Verify-Package.ps1
```

It packs a unique local version, restores a separate host/library pair into a fresh package
cache, builds and runs it, then verifies that an invalid declaration produces TFG002. The
consumer checks defaults, automatic DI across initialized assemblies, shared updates, and root
catalog composition. The generator DLL must not be copied to the application's runtime output.
Artifacts and the expected failing build log remain under `artifacts/package-tests`.
The script needs NuGet access for Microsoft DI dependencies and does not publish packages.

See [the working agreement](WORKING-AGREEMENT.md) and [the slice plan](PLAN.md).
