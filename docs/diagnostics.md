# Diagnostics

The generator reports every unsupported declaration at compile time instead of silently ignoring
it or failing at runtime. A provider with any of these issues contributes no partial definition.

| ID | Severity | Meaning |
| --- | --- | --- |
| `TFG001` | Error | Unsupported provider shape |
| `TFG002` | Error | Unsupported property shape or type |
| `TFG003` | Error | Missing, null, or nonconstant default |
| `TFG004` | Error | Generated class name conflicts |
| `TFG005` | Error | Generated catalog name conflicts |

## TFG001 — Unsupported provider shape

The class implements `IFeatureProvider` but is not a plain top-level concrete class: it is a
`record`, `struct`, `abstract` class, generic, nested, file-local, or inherits from another class.

```csharp
public abstract class Checkout : IFeatureProvider // TFG001
{
    public bool NuevoCheckout => false;
}
```

Fix: make it a plain, top-level, non-generic, non-abstract, non-inherited class (or `partial`
piece of one).

## TFG002 — Unsupported property shape or type

A property on a valid provider is not public/instance/read-only/non-indexed, or is not `bool` or
non-nullable `string`.

```csharp
public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout { get; set; } = false; // TFG002: has a setter
    public int MaxItems => 10;                        // TFG002: unsupported type
}
```

Fix: make the property `bool` or `string`, read-only (`=>`, a `get`-only accessor, or
`{ get; } = constant`), public, non-indexed.

## TFG003 — Missing, null, or nonconstant default

The getter does not resolve to a non-null compile-time constant.

```csharp
public sealed class Checkout : IFeatureProvider
{
    public string TextoBoton => GetConfiguredLabel(); // TFG003: not constant
    public string? Placeholder => null;                 // TFG003: null default
}
```

Fix: return a literal, a `const` reference, `nameof`, or another constant expression. Bodies
beyond `=> constant`, `{ get => constant; }`, `{ get { return constant; } }`, and
`{ get; } = constant` are rejected even when they would still be constant-foldable.

## TFG004 — Generated class name conflicts

The generated access class name (`<Namespace>.<ProviderName>FeatureFlags`) collides with an
existing type, namespace, or one of the provider's own feature properties.

```csharp
namespace MyApp;

public sealed class CheckoutFeatureFlags { } // already exists

public sealed class Checkout : IFeatureProvider // TFG004: generated name collides
{
    public bool NuevoCheckout => false;
}
```

Fix: rename the provider, or remove/rename the conflicting type.

## TFG005 — Generated catalog name conflicts

The assembly's generated internal catalog type name collides with an existing type in that
assembly. Fix the same way as `TFG004`: rename whichever side is causing the collision.
