# TinyFlags

Typed feature flag declarations for .NET.

## Current behavior

The generator discovers feature providers, reads their flag definitions, and reports unsupported
declarations at compile time. `FeatureValues` provides local value resolution. Generated access
classes, networking, dependency injection, and NuGet distribution are not implemented yet.

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
defaults; calling them directly still returns those defaults. The agreed future generated
class is `CheckoutFeatureFlags`, whose getters will delegate to the client's `FeatureValues`.

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

## Components

```text
src/TinyFlags                  Marker and local values (net8.0)
src/TinyFlags.SourceGen        Incremental generator (netstandard2.0)
tests/TinyFlags.Tests
tests/TinyFlags.SourceGen.Tests
```

The implemented flow is discovery -> analysis -> validation -> definition model.
`Discovery` performs the initial syntax filter. `Analysis` resolves the marker, combines partial
members, and extracts flag information. The generator entry point connects these phases through
Roslyn's syntax predicate and semantic transform.
Roslyn symbols stay inside `Analysis`. Models and validation have no Roslyn dependencies;
source positions and issues are plain data. The generator entry point and diagnostic reporter
adapt these results to Roslyn. Planning and emission come in later slices.

The incremental pipeline analyzes and validates providers individually. Models compare by
value, including their collections, so equivalent results reuse downstream work. Partial
declarations contribute one provider. Roslyn may repeat semantic analysis after an edit;
validation is reused when the extracted model is unchanged. Diagnostic reporting binds
locations to the current compilation.

Tests use real Roslyn compilations and the actual marker assembly. They verify extracted
definitions, constant values, marker identity, diagnostic IDs, and diagnostic locations.
Incremental tests reuse the same driver across edits and inspect tracked step results to verify
cache reuse and invalidation, including external constants and partial declarations.
The test project invokes the generator directly; automatic inclusion in a consumer NuGet
package is a later slice.

```shell
dotnet test TinyFlags.slnx
```

See [the working agreement](WORKING-AGREEMENT.md) and [the slice plan](PLAN.md).
