# Single-service HTTP sample

The smallest possible runnable example of [Option B: Connect to a server](../../docs/getting-started.md#3-register)
from Getting Started - one service, one flag declaration, polling a real `TinyFlags.Server` over
HTTP. If you want the cross-service story instead (two services sharing one flag identity), see
[MultiServiceHttp](../MultiServiceHttp/README.md).

`Checkout.cs` is exactly the declaration from the docs:

```csharp
public sealed class Checkout : IFeatureProvider
{
    public bool NuevoCheckout => false;
    public string TextoBoton => "Comprar";
}
```

## 1. Start everything

```shell
cd samples/SingleServiceHttp
docker compose up -d --wait --build
```

One command brings up Postgres, `TinyFlags.Server`, and the app - migrated, seeded, and
registered, nothing to copy-paste. A one-shot `bootstrap` step applies migrations, creates a
project/environment/client key, and issues an admin token, handing the generated secrets to the
app over a shared volume; `--wait` blocks until everyone's actually up.

## 2. See its current values

```shell
curl http://localhost:8096/flags
```

```json
{"nuevoCheckout":false,"textoBoton":"Comprar"}
```

The app registered its declared flags at startup and is already polling `TinyFlags.Server` for
value changes every 5 seconds (the sample shortens `RefreshInterval` from its 30-second default -
see [value-synchronization.md](../../docs/value-synchronization.md) - purely so the next step
doesn't feel like it's hanging).

## 3. Change a value and watch it picked up

Open `http://localhost:8095/Admin/Login`. The admin token was written to the `bootstrap` step's
log - grab it with:

```shell
docker compose logs bootstrap
```

Sign in, go to **Flags**, pick the **Development** environment, and flip `NuevoCheckout` to
`True`. Within a few seconds:

```shell
curl http://localhost:8096/flags
```

reports `"nuevoCheckout":true` - the running process picked up a value it never had at startup,
with no restart and no redeploy.

## Cleanup

```shell
docker compose down -v
```

Removes every container and both named volumes (the database and the handed-off secrets).
Separate ports and volumes from every other sample here, so this can run alongside them.

## Why the app shares `tinyflags-server`'s network

TinyFlags clients require HTTPS or a loopback endpoint - a deliberate guard against leaking an API
key over plain HTTP to a non-local host. `app` runs with `network_mode: "service:tinyflags-server"`,
so `http://localhost:8080` from inside it is genuinely loopback, not a bypass of that check. It
listens on `8082`, not `8081` - `TinyFlags.Server` always binds `8081` itself for gRPC, whether or
not a given sample uses it.

## See also

- [Getting Started](../../docs/getting-started.md) - the walkthrough this sample runs
- [SingleServiceGrpc](../SingleServiceGrpc/README.md) - the same idea, real-time push instead of
  polling
- [MultiServiceHttp](../MultiServiceHttp/README.md) - two services sharing one flag identity
