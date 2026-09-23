# Sharing a flag across services

There is no "shared" flag setting to configure. A flag is shared across services simply because
two independent declarations produce the same identity — nothing more.

## Why it works

Flag identity is a string: the fully qualified provider type name plus the property name (for
example `MyApp.Checkout.NuevoCheckout`), computed independently by the generator in each
compilation. It does not depend on runtime type identity, a shared assembly, or any server-side
concept of "service". Two completely separate codebases produce the same key purely by writing the
same namespace, class name, and property name.

## How to do it

Declare the identical provider in both services. Each only needs its own reference to TinyFlags —
no reference to each other, no shared internal package required for this to work:

```csharp
// OrdersService
namespace MyApp;

public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
}
```

```csharp
// PaymentsService
namespace MyApp;

public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
}
```

Both generate their own `MyApp.CheckoutFeatureFlags` locally. Both
[register](registration.md) the same key, `MyApp.Checkout.NuevoCheckout`, as part of their own
composed catalog. Both [read the same value](value-synchronization.md) from the environment,
because it is one row on the server, not two.

Registration is idempotent for this case: replaying the same key with the same `Kind` and the same
declared default is a no-op, not a conflict, no matter which service registers first or how many
times. If the two declarations disagree — different type (`bool` vs `string`) or a different
default — that *is* a conflict: the whole registering batch is rejected (`409` over HTTP,
`AlreadyExists` over gRPC), exactly as it would be for any other definition mismatch. See
[Registration](registration.md) for the exact rule.

## Making it intentional, not accidental

This is a naming convention, not something the generator can enforce across assemblies — it has no
visibility into other projects' source. Two teams who happen to pick the same namespace, class, and
property name without meaning to share will share anyway, silently.

To make sharing a deliberate decision rather than a coincidence, put the provider class in a
package both services already reference and document that it is a shared flag — a comment on the
class, a line in your own onboarding docs, whatever your team already uses for cross-service
contracts. TinyFlags does not need that reference to exist for sharing to work; it only helps
people, not the generator.

## Try it

[`samples/MultiServiceHttp`](../samples/MultiServiceHttp/README.md) runs two independent services against
a Dockerized `TinyFlags.Server`, each with its own private flag plus one identical, shared
declaration. `docker compose up`, then flip the shared value in the admin dashboard: `curl` both
services' `/flags` endpoint and watch them agree.
