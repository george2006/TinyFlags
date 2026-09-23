# Documentation

If you only read one thing first, start with **Getting Started** and then skim **Architecture**.

Key capabilities (high level):

- compile-time typed flags, generated from `IFeatureProvider` declarations
- local-only mode (`FeatureValues`), or server-synced through a transport — `TinyFlags.Http`
  (registration + polling) or `TinyFlags.Grpc` (registration + real-time push) are both real,
  independent reference implementations, against `TinyFlags.Server` (a separate, privately-hosted
  repo) or your own server; see [Building a Transport](building-a-transport.md) to add another
- revisioned values, with conditional `GET`/`ETag` for polling or a live push stream for real-time
- background registration and value-sync workers, independent of each other regardless of transport
- authenticated, environment-scoped access with separate register/read permissions
- resilient by default: transient failures retry or reconnect, permanent failures stop cleanly,
  local values never disappear

## Status

Early alpha, published on NuGet.org as `TinySuite.TinyFlags` (see the top-level
[README](../README.md#install) for install commands and why the package ID differs from the
product name). The NuGet client targets net8.0 so applications can use the same package before and
after upgrading to whatever the server targets.

Core docs:

- [Getting Started](getting-started.md)
- [Architecture](architecture.md)
- [Registration](registration.md)
- [Value Synchronization](value-synchronization.md)
- [Server Protocol (HTTP)](protocol.md)
- [Server Protocol (gRPC)](grpc-protocol.md)
- [Building a Transport](building-a-transport.md)
- [Sharing a Flag Across Services](multi-service-flags.md)
- [Diagnostics](diagnostics.md)
- [Tiny suite](tiny-suite.md)
