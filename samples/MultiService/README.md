# Multi-service sample

Two independent .NET processes, `OrdersService` and `PaymentsService`, each with their own
private flag, plus one flag both declare identically and therefore share — backed by a real
`TinyFlags.Server` running in Docker. This is [Sharing a Flag Across
Services](../../docs/multi-service-flags.md) made runnable.

| Flag | Declared by | Shared? |
| --- | --- | --- |
| `OrdersService.Checkout.ExpressCheckout` (Boolean) | OrdersService only | No — private |
| `PaymentsService.Provider.PreferredGateway` (String) | PaymentsService only | No — private |
| `Shared.Promotions.HolidaySaleBanner` (Boolean) | Both, identically | Yes |

Neither service references the other, and there is no shared package between them. The shared
flag is shared purely because both declarations produce the same identity — see
[multi-service-flags.md](../../docs/multi-service-flags.md) for why that's enough.

## 1. Start Postgres and the server

```shell
cd samples/MultiService
docker compose up -d --wait
```

This builds `TinyFlags.Server`'s [Dockerfile](../../src/TinyFlags.Server/Dockerfile) and starts it
on `http://localhost:8085`, backed by Postgres on `localhost:54326` — separate ports from the main
[local development stack](../../docs/server.md#local-development) so both can run at once.

## 2. Apply migrations (one time)

The server never migrates its own database on startup — see [Running the
Server](../../docs/server.md). From the repo root:

```shell
dotnet tool restore
ConnectionStrings__TinyFlags="Host=localhost;Port=54326;Database=tinyflags;Username=tinyflags;Password=tinyflags-sample" \
  dotnet ef database update --project src/TinyFlags.Server -- --environment Development
```

## 3. Create a project, environment, and client key

```shell
ConnectionStrings__TinyFlags="Host=localhost;Port=54326;Database=tinyflags;Username=tinyflags;Password=tinyflags-sample" \
  dotnet run --project src/TinyFlags.Server -- --environment Development --seed-development
```

Copy the printed `tf_...` secret — both services authenticate with it.

## 4. Issue an admin token

```shell
ConnectionStrings__TinyFlags="Host=localhost;Port=54326;Database=tinyflags;Username=tinyflags;Password=tinyflags-sample" \
  dotnet run --project src/TinyFlags.Server -- --environment Development --issue-admin-key --label "Sample"
```

Copy the printed `tfa_...` secret — you'll use it to sign into the dashboard in step 6.

## 5. Run both services

In two separate terminals, from the repo root, using the `tf_...` secret from step 3:

```shell
TinyFlags__Endpoint="http://localhost:8085" TinyFlags__ApiKey="tf_..." dotnet run --project samples/MultiService/OrdersService
```

```shell
TinyFlags__Endpoint="http://localhost:8085" TinyFlags__ApiKey="tf_..." dotnet run --project samples/MultiService/PaymentsService
```

Each registers its own declared flags once at startup, then polls for value changes every 5
seconds (the sample shortens `RefreshInterval` from its 30-second default — see
[value-synchronization.md](../../docs/value-synchronization.md) — purely so the demo doesn't feel
like it's hanging). You'll see both terminals printing their current values, still at their
declared defaults.

## 6. Change a value and watch both services pick it up

Open `http://localhost:8085/Admin/Login` and sign in with the `tfa_...` admin token. Go to
**Flags**, pick the **Development** environment, and flip `Shared.Promotions.HolidaySaleBanner` to
`True`.

Within a few seconds, **both terminals** print `Shared.Promotions.HolidaySaleBanner=True` — two
unrelated processes reading the same server-side value. Flip `ExpressCheckout` or
`PreferredGateway` instead, and only the matching service's terminal changes: those two are
private to their own service.

## Cleanup

```shell
cd samples/MultiService
docker compose down -v
```

This removes the containers and the sample's database volume. Nothing here touches the [main
local development stack](../../docs/server.md#local-development).
