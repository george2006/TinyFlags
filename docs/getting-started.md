# Getting started

This guide shows the minimum steps to wire TinyFlags into an application, local-only first, then
connected to a server.

## 1) Install

Not yet published to NuGet.org. Pack and reference it locally:

```bash
dotnet pack src/TinyFlags/TinyFlags.csproj -c Release -o artifacts/packages
dotnet add package TinyFlags --source artifacts/packages
```

## 2) Declare a flag provider

A provider is a plain class implementing the `IFeatureProvider` marker, with one property per
flag:

```csharp
using TinyFlags;

namespace MyApp;

public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
```

Only `bool` and non-nullable `string` properties with constant defaults become flags. See
[Architecture](architecture.md#supported-declarations) for the complete shape rules and every
`TFG0xx` diagnostic.

Want the same flag read by more than one service? Declare the identical provider — same
namespace, class, and property name — in each one. No shared library or server configuration
needed. See [Sharing a Flag Across Services](multi-service-flags.md).

The generator produces a public sealed access class next to your declaration:
`MyApp.CheckoutFeatureFlags`, constructed with `FeatureValues`.

## 3) Register

### Option A: Local-only

```csharp
using Microsoft.Extensions.DependencyInjection;
using TinyFlags;

var services = new ServiceCollection();
services.AddTinyFlags();
```

This registers a shared `FeatureValues` singleton and every generated access class from
initialized assemblies. No network calls happen anywhere in this mode. Flags read their declared
defaults unless something else calls `FeatureValues.ReplaceSnapshot` directly.

### Option B: Connect to a server

Also reference the [TinyFlags.Http](../src/TinyFlags.Http) package — the reference transport, one
implementation of the transport contracts (`IFeatureDefinitionsTransport`, `IFeatureValuesTransport`)
in the core package's `Abstractions/` folder:

```csharp
builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseHttpTransport(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]!);
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"];
}));
```

This does everything Option A does, plus two independent background workers started after
`ApplicationStarted`:

- a **registration worker** that sends your assemblies' declared catalog to the server, retrying
  transient failures until it succeeds, is permanently denied, or the host shuts down;
- a **synchronization worker** that fetches the environment's current values and keeps them
  refreshed on a recurring interval.

Neither worker blocks startup or a flag getter. See [Registration](registration.md) and
[Value Synchronization](value-synchronization.md) for what each one actually does.

### Option C: connect to a server, real-time

Reference [TinyFlags.Grpc](../src/TinyFlags.Grpc) instead of `TinyFlags.Http` for push updates
instead of polling — same registration worker, but values arrive the moment they change rather
than on the next interval:

```csharp
builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseGrpcTransport(options =>
{
    options.Endpoint = new Uri(builder.Configuration["TinyFlags:Endpoint"]!);
    options.ApiKey = builder.Configuration["TinyFlags:ApiKey"];
}));
```

Both options implement the same core contracts — see [Building a Transport](building-a-transport.md)
if you want a third one, for your own protocol or your own server.

## 4) Read flags

```csharp
using var provider = services.BuildServiceProvider();
var flags = provider.GetRequiredService<MyApp.CheckoutFeatureFlags>();

bool enabled = flags.NuevoCheckout;
string label = flags.TextoBoton;
```

Each read consults the current snapshot. In local-only mode that's always the declared default. In
server-synced mode it's whatever the synchronization worker last published — the same generated
instance observes updates automatically. No re-resolution needed.

## 5) Run the server

To try Option B or C end to end, you need a running server and an issued API key: either
[`TinyFlags.Server`](https://github.com/george2006/TinyFlags.Server), the reference
implementation, or your own, speaking the same wire contracts ([HTTP](protocol.md) or
`tinyflags.proto` for gRPC). Three runnable samples, each a minimal Docker Compose stack:

- [Single-Service HTTP Sample](../samples/SingleServiceHttp/README.md) — this Option B code,
  actually running
- [Single-Service gRPC Sample](../samples/SingleServiceGrpc/README.md) — this Option C code,
  actually running
- [Multi-Service HTTP Sample](../samples/MultiServiceHttp/README.md) — two services sharing one
  flag identity over HTTP
