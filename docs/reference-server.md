# The reference server

`TinyFlags.Server` is the reference implementation of both wire protocols ([HTTP](protocol.md),
[gRPC](grpc-protocol.md)) — the server every sample in this repo runs against. This page is about
using it, not implementing it; see [Building a Transport](building-a-transport.md) if you'd rather
implement your own server instead.

It's also a real user of the rest of the [Tiny suite](tiny-suite.md), not just a demo: TinyDispatcher
for command/query handling, TinyValidations for input validation, and TinyEvents for the durable
outbox behind its gRPC push notifications. None of that is a dependency your application takes on
by using the TinyFlags client — it's just what the reference server happens to be built with.

## Private source, public image

`TinyFlags.Server`'s source repo is private, but it ships as a real, published image,
`ghcr.io/george2006/tinyflags-server`, pullable by anyone with no source access or login
required — every sample here runs it exactly that way. You don't need access to that repo to use
TinyFlags against a real server; `docker pull` is enough.

## Licensing

The server requires a license key to start (`TinyFlags__License`). The samples ship with a real
evaluation license baked into their `docker-compose.yml`, valid for about a week, purely so they
work with zero setup — once it expires, the server refuses to start rather than degrading
silently. A fresh license is a message away for now; there's no self-serve issuing flow yet.

## Why the source is private

Keeping `TinyFlags.Server` as a possible commercial product later is a real option being kept
open — not a decision that's been made, just one that hasn't been ruled out.

That's exactly why the transport is a public contract in the first place: even if the reference
server becomes paid, you're never locked into it. TinyFlags itself, the source generator, and both
reference transports are MIT-licensed and stay that way regardless of what happens with the
server. Implement your own against the documented wire protocol and keep every flag you've already
declared — see [Building a Transport](building-a-transport.md).

## See also

- [Server Protocol (HTTP)](protocol.md), [Server Protocol (gRPC)](grpc-protocol.md) — the
  contracts `TinyFlags.Server` implements
- [Single-Service HTTP Sample](../samples/SingleServiceHttp/README.md),
  [Single-Service gRPC Sample](../samples/SingleServiceGrpc/README.md),
  [Multi-Service HTTP Sample](../samples/MultiServiceHttp/README.md) — running it for real
- [TinyFlags.Server](https://github.com/george2006/TinyFlags.Server) — the repo itself (private;
  linked for completeness, not because you need access to it)
