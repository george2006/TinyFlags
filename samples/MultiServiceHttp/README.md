# Multi-service HTTP sample

Two independent HTTP services, `OrdersService` and `PaymentsService`, each with their own private
flag, plus one flag both declare identically and therefore share — backed by a real
`TinyFlags.Server`. Everything runs in Docker, wired up by one command. This is [Sharing a Flag
Across Services](../../docs/multi-service-flags.md) made runnable.

| Flag | Declared by | Shared? |
| --- | --- | --- |
| `OrdersService.Checkout.ExpressCheckout` (Boolean) | OrdersService only | No — private |
| `PaymentsService.Provider.PreferredGateway` (String) | PaymentsService only | No — private |
| `Shared.Promotions.HolidaySaleBanner` (Boolean) | Both, identically | Yes |

Neither service references the other, and there is no shared package between them. The shared
flag is shared purely because both declarations produce the same identity — see
[multi-service-flags.md](../../docs/multi-service-flags.md) for why that's enough.

## 1. Start everything

```shell
cd samples/MultiServiceHttp
docker compose up -d --wait --build
```

One command brings up Postgres, `TinyFlags.Server`, and both sample services — migrated, seeded,
and registered, nothing to copy-paste. A one-shot `bootstrap` step applies migrations, creates a
project/environment/client key, and issues an admin token, handing the generated secrets to the
two services over a shared volume; `--wait` blocks until everyone's actually up.

## 2. See their current values

```shell
curl http://localhost:8090/flags
curl http://localhost:8091/flags
```

```json
{"expressCheckout":false,"sharedHolidaySaleBanner":false}
{"preferredGateway":"Stripe","sharedHolidaySaleBanner":false}
```

Both registered their declared flags at startup and are already polling `TinyFlags.Server` for
value changes every 5 seconds (the sample shortens `RefreshInterval` from its 30-second default —
see [value-synchronization.md](../../docs/value-synchronization.md) — purely so the next step
doesn't feel like it's hanging).

## 3. Change a value and watch both services pick it up

Open `http://localhost:8085/Admin/Login`. The admin token was written to the `bootstrap` step's
log — grab it with:

```shell
docker compose logs bootstrap
```

Sign in, go to **Flags**, pick the **Development** environment, and flip
`Shared.Promotions.HolidaySaleBanner` to `True`. Within a few seconds:

```shell
curl http://localhost:8090/flags
curl http://localhost:8091/flags
```

**both** now report `"sharedHolidaySaleBanner":true` — two unrelated processes reading the same
server-side value. Flip `ExpressCheckout` or `PreferredGateway` instead, and only the matching
service's response changes: those two are private to their own service.

## Cleanup

```shell
docker compose down -v
```

Removes every container and both named volumes (the database and the handed-off secrets). Nothing
here touches the [main local development stack](../../docs/server.md#local-development) — separate
ports, separate volumes.

## Why it needs a `--migrate` step at all

`TinyFlags.Server` deliberately never migrates its own database on ordinary startup — see [Running
the Server](../../docs/server.md). The `bootstrap` service calls the explicit, opt-in `--migrate`
command instead, which applies migrations from the already-published app with no `dotnet-ef` or
source checkout required — the same thing you'd reach for in a real container-only deployment.

## Why the services share `tinyflags-server`'s network

TinyFlags clients require HTTPS or a loopback endpoint — a deliberate guard against leaking an API
key over plain HTTP to a non-local host. `orders-service` and `payments-service` run with
`network_mode: "service:tinyflags-server"`, so `http://localhost:8080` from inside either of them
is genuinely loopback, not a bypass of that check.
