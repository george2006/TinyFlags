# TinyFlags

Typed feature flag declarations for .NET.

The code brings the flags. The server only serves their values.

## What you get

- **Compile-time typed flags** — declare a flag as a C# property; the generator produces a typed
  access class. No string keys at the call site.
- **No dashboard-created flags** — a flag exists because it is declared in code. The server stores
  values for flags that already exist; it never originates one.
- **Local-only or server-synced** — run entirely in-process with `FeatureValues`, or connect to
  `TinyFlags.Server` for centrally managed values.
- **Revisioned, conditional sync** — the SDK polls on a jittered interval using conditional GET
  (`ETag`); unchanged values cost a 304 with no body.
- **Resilient by default** — transient failures retry with backoff; a server outage or malformed
  response falls back to the last known values, never to an exception.
- **Multi-assembly composition** — each assembly contributes its own flag catalog; the host
  composes them at startup.
- **Source-generator diagnostics** — invalid declarations fail at compile time (`TFG001`-`TFG005`),
  not at runtime.

## Install

TinyFlags is not yet published to NuGet.org (current version: `0.1.0-dev`). Pack and reference it
locally:

```bash
dotnet pack src/TinyFlags/TinyFlags.csproj -c Release -o artifacts/packages
dotnet add package TinyFlags --source artifacts/packages

# Only if you want the reference HTTP transport (talking to TinyFlags.Server or your own):
dotnet pack src/TinyFlags.Http/TinyFlags.Http.csproj -c Release -o artifacts/packages
dotnet add package TinyFlags.Http --source artifacts/packages
```

## Quick start

Declare a flag provider:

```csharp
using TinyFlags;

namespace MyApp;

public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
```

The generator produces a typed access class, `MyApp.CheckoutFeatureFlags`. Register it and read it:

```csharp
using Microsoft.Extensions.DependencyInjection;
using TinyFlags;

var services = new ServiceCollection();
services.AddTinyFlags();
using var provider = services.BuildServiceProvider();

var flags = provider.GetRequiredService<MyApp.CheckoutFeatureFlags>();
bool enabled = flags.NuevoCheckout; // false, the declared default
```

That is local-only: flags read their declared defaults, with no network calls. To connect to a
`TinyFlags.Server` environment and receive centrally managed values, also reference the
[TinyFlags.Http](src/TinyFlags.Http) package, the reference transport:

```csharp
builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]!);
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"];
}));
```

This registers your assemblies' declared flags with the server in the background after startup,
and keeps values synchronized on a recurring interval. HTTP is just one implementation of
TinyFlags' transport contracts (`IFeatureDefinitionsTransport`, `IFeatureValuesTransport`,
`IFeatureValuesSubscription`, in the core package) — see
[Getting Started](docs/getting-started.md) for the complete walkthrough.

## Supported declarations

- Top-level concrete classes, public or internal, implementing the `IFeatureProvider` marker.
  Partial classes are combined into one provider.
- Properties must be public, instance, read-only, non-indexed, and either `bool` or non-nullable
  `string`, with a non-null compile-time constant default.
- Records, structs, abstract, nested, generic, inherited, and file-local providers are not
  supported. Helpers and constant fields do not become flags.

Full rules and every diagnostic ID are in [Architecture](docs/architecture.md).

## Server-synced values

Once connected, one worker keeps `FeatureValues` fresh — which one depends on your transport.
`TinyFlags.Http` polls: fetches after startup, refreshes on a ~30 second interval plus jitter,
conditional `GET` so an unchanged read costs nothing but a 304. `TinyFlags.Grpc` pushes instead: no
polling interval, the server sends a fresh snapshot the moment something changes, real-time in the
tens-of-milliseconds range. Either way, a server outage, a denied read, or a malformed response
never breaks your app — the last known values stay in effect.

See [Value Synchronization](docs/value-synchronization.md) for the full picture: revisions,
cursors, retry/reconnect, and how permanent failures are told apart from recoverable ones.

## Registration

Applications declare their flags in code. Whichever transport you use registers each assembly's
catalog with the server in the background after startup. The server never creates a flag — only
code does.

See [Registration](docs/registration.md) for the catalog composition model, the wire contracts,
and authentication.

## Documentation

- [Getting Started](docs/getting-started.md)
- [Architecture](docs/architecture.md)
- [Registration](docs/registration.md)
- [Value Synchronization](docs/value-synchronization.md)
- [Server Protocol (HTTP)](docs/protocol.md)
- [Building a Transport](docs/building-a-transport.md)
- [Sharing a Flag Across Services](docs/multi-service-flags.md)
- [Multi-Service Sample](samples/MultiService/README.md) — a runnable walkthrough, two services and a Dockerized server
- [Diagnostics](docs/diagnostics.md)
- [Tiny suite](docs/tiny-suite.md)

## Diagnostics

| ID | Severity | Meaning |
| --- | --- | --- |
| `TFG001` | Error | Unsupported provider shape |
| `TFG002` | Error | Unsupported property shape or type |
| `TFG003` | Error | Missing, null, or nonconstant default |
| `TFG004` | Error | Generated class name conflicts |
| `TFG005` | Error | Generated catalog name conflicts |

Examples and the exact rule behind each one are in [Diagnostics](docs/diagnostics.md).

## Tiny suite

TinyFlags belongs to the Tiny suite:

| Project | Kind | Responsibility |
| --- | --- | --- |
| [TinyDispatcher](https://github.com/george2006/TinyDispatcher) | Library | Command and query execution |
| [TinyValidations](https://github.com/george2006/TinyValidations) | Library | Application input validation |
| [TinyEvents](https://github.com/george2006/TinyEvents) | Library | Reliable application-event handling through the outbox pattern |
| [TinyFlags](https://github.com/george2006/TinyFlags) | Library | Typed feature flag declarations |
| [TheTinyApplicationLayer](https://github.com/george2006/TheTinyApplicationLayer) | Example | Runnable ASP.NET Core and Blazor application using the suite |

Same author, same philosophy: compile-time correctness over runtime string keys. TinyFlags has no
SDK-level dependency on the other libraries. Its reference server, `TinyFlags.Server`, is a
separate, privately-hosted repo — this one only knows the wire contracts it speaks (HTTP or gRPC,
see [Building a Transport](docs/building-a-transport.md)), not its implementation.
`TheTinyApplicationLayer` does not include a flags example yet — that is planned once this feature
set settles. See [Tiny suite](docs/tiny-suite.md).

## When to use

TinyFlags is a good fit when you want:

- feature flags the compiler checks, not string keys that fail at runtime
- a clear line between *defining* a flag (code, reviewed, deployed) and *changing its value*
  (server, centrally managed)
- a small dependency surface — no vendor SDK beyond DI/Hosting abstractions
- resilience by default — your app never breaks because the flags server is unreachable

It is not a fit if you want to create or delete flags from a dashboard without a deploy — that is
a deliberate non-goal, not a missing feature.

## Test Coverage & Hardening

TinyFlags is verified with real collaborators, not mocks: real Roslyn compilations for the
generator, and a real loopback HTTP server for the SDK's registration/synchronization workers.

| Component | Tests |
| --- | --- |
| Source generator | 117 |
| SDK runtime | 181 |
| **Total** | **298** |

Run everything:

```shell
dotnet test TinyFlags.slnx -warnaserror
```

Requires a .NET SDK (net8.0 or later), PowerShell, and NuGet access for the packaging
verification script.

## Components

```text
src/TinyFlags                  Marker, local values, catalog composition, transport contracts (net8.0)
src/TinyFlags.Http              Reference HTTP transport implementation (net8.0)
src/TinyFlags.SourceGen        Incremental generator (netstandard2.0)
tests/TinyFlags.Tests
tests/TinyFlags.Http.Tests
tests/TinyFlags.SourceGen.Tests
```

See [the working agreement](WORKING-AGREEMENT.md) and [the slice plan](PLAN.md).
