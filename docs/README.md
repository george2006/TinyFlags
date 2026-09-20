# Documentation

If you only read one thing first, start with **Getting Started** and then skim **Architecture**.

Key capabilities (high level):

- compile-time typed flags, generated from `IFeatureProvider` declarations
- local-only mode (`FeatureValues`) or server-synced mode (`TinyFlags.Server`)
- revisioned values with conditional GET (`ETag`) and jittered recurring refresh
- background registration and synchronization workers, independent of each other
- authenticated, environment-scoped HTTP with separate register/read permissions
- resilient by default: transient failures retry, permanent failures stop cleanly, local values
  never disappear

## Status

`0.1.0-dev`. Not yet published to NuGet.org; pack and reference it locally (see the top-level
[README](../README.md#install)). `TinyFlags.Server` targets net10.0; the NuGet client targets
net8.0 so applications can use the same package before and after upgrading.

Core docs:

- [Getting Started](getting-started.md)
- [Architecture](architecture.md)
- [Registration](registration.md)
- [Value Synchronization](value-synchronization.md)
- [Sharing a Flag Across Services](multi-service-flags.md)
- [Running the Server](server.md)
- [Admin Dashboard](admin-dashboard.md)
- [Diagnostics](diagnostics.md)
- [Tiny suite](tiny-suite.md)
