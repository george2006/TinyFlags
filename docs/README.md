# Documentation

If you only read one thing first, start with **Getting Started** and then skim **Architecture**.

Key capabilities (high level):

- compile-time typed flags, generated from `IFeatureProvider` declarations
- local-only mode (`FeatureValues`) or server-synced mode, against `TinyFlags.Server` (a
  separate, privately-hosted repo) or your own server speaking the same
  [wire protocol](protocol.md)
- revisioned values with conditional GET (`ETag`) and jittered recurring refresh
- background registration and synchronization workers, independent of each other
- authenticated, environment-scoped HTTP with separate register/read permissions
- resilient by default: transient failures retry, permanent failures stop cleanly, local values
  never disappear

## Status

`0.1.0-dev`. Not yet published to NuGet.org; pack and reference it locally (see the top-level
[README](../README.md#install)). The NuGet client targets net8.0 so applications can use the same
package before and after upgrading to whatever the server targets.

Core docs:

- [Getting Started](getting-started.md)
- [Architecture](architecture.md)
- [Registration](registration.md)
- [Value Synchronization](value-synchronization.md)
- [Server Protocol](protocol.md)
- [Sharing a Flag Across Services](multi-service-flags.md)
- [Diagnostics](diagnostics.md)
- [Tiny suite](tiny-suite.md)
