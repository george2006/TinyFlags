# Single-service gRPC sample

The smallest possible runnable example of [Option C: connect to a server, real-time](../../docs/getting-started.md#3-register)
from Getting Started - one service, one flag declaration, receiving values pushed over a real
`TinyFlags.Server` gRPC stream instead of polling for them. Same flag declaration as
[SingleServiceHttp](../SingleServiceHttp/README.md), the only difference is the transport:

```csharp
builder.Services.AddTinyFlags(tinyFlags => tinyFlags.UseGrpcTransport(options =>
{
    options.Endpoint = new Uri(...);
    options.ApiKey = ...;
}));
```

## 1. Start everything

```shell
cd samples/SingleServiceGrpc
docker compose up -d --wait --build
```

One command brings up Postgres, `TinyFlags.Server`, and the app - migrated, seeded, and
registered, nothing to copy-paste. A one-shot `bootstrap` step applies migrations, creates a
project/environment/client key, and issues an admin token, handing the generated secrets to the
app over a shared volume; `--wait` blocks until everyone's actually up.

## 2. See its current values

```shell
curl http://localhost:8098/flags
```

```json
{"nuevoCheckout":false,"textoBoton":"Comprar"}
```

The app registered its declared flags at startup and opened a `Watch` stream against
`TinyFlags.Server`'s gRPC endpoint - there's no `RefreshInterval` here, nothing to configure:
updates arrive the moment they happen, not on the next poll.

## 3. Change a value and watch it arrive instantly

Open `http://localhost:8097/Admin/Login`. The admin token was written to the `bootstrap` step's
log - grab it with:

```shell
docker compose logs bootstrap
```

Sign in, go to **Flags**, pick the **Development** environment, and flip `NuevoCheckout` to
`True`. Immediately (no multi-second wait, unlike the HTTP sample's 5-second poll interval):

```shell
curl http://localhost:8098/flags
```

reports `"nuevoCheckout":true` - pushed to the running process over the open stream, not fetched
by it.

## Cleanup

```shell
docker compose down -v
```

Removes every container and both named volumes (the database and the handed-off secrets).
Separate ports and volumes from every other sample here, so this can run alongside them.

## Why the app shares `tinyflags-server`'s network, and uses port 8081

TinyFlags clients require HTTPS or a loopback endpoint - a deliberate guard against leaking an API
key over plain HTTP to a non-local host. `app` runs with `network_mode: "service:tinyflags-server"`,
so `http://localhost` from inside it is genuinely loopback, not a bypass of that check.
`TinyFlags.Server` always binds two separate ports: `8080` for REST/the admin dashboard,
`8081` for gRPC - this sample's `TinyFlags__Endpoint` points at `8081`, not `8080`, since the gRPC
transport speaks `tinyflags.proto` directly rather than HTTP JSON. The app's own port is `8082`,
avoiding both.

## See also

- [Getting Started](../../docs/getting-started.md) - the walkthrough this sample runs
- [SingleServiceHttp](../SingleServiceHttp/README.md) - the same idea, polling instead of push
- [MultiServiceHttp](../MultiServiceHttp/README.md) - two services sharing one flag identity
