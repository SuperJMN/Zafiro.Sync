# Server Deployment

This document describes how to run the Zafiro.Sync server that is implemented today in `AppFileSync.Api`.

The deployed service is:

```text
AppFileSync.Api
  - ASP.NET Core 10
  - PostgreSQL through EF Core/Npgsql
  - AppIdentity challenge/session auth
  - app registration from configuration
  - encrypted payload storage in PostgreSQL
```

There is no user database. The server only validates possession of an Ed25519 private key and stores opaque data under:

```text
owner_subject = ed25519:{publicKey}
app_id        = configured app id
file_id       = opaque client-derived id
```

## Runtime Requirements

- .NET 10 runtime for `AppFileSync.Api`.
- PostgreSQL.
- HTTPS at the ingress/proxy layer for any non-local deployment.
- One or more configured apps under `AppFileSync:Apps`.

## Configuration

Configuration can come from `appsettings`, environment variables, Kubernetes `ConfigMap`, user secrets, or any ASP.NET Core configuration provider.

| Key | Required | Description |
| --- | --- | --- |
| `ConnectionStrings__Postgres` | Yes | PostgreSQL connection string used by EF Core. |
| `AppFileSync__MigrateOnStartup` | No | Applies EF migrations on startup when `true`. Use only for controlled deployments. |
| `AppFileSync__Apps__0__AppId` | Yes | Public app namespace, for example `fifo-calculator`. |
| `AppFileSync__Apps__0__DisplayName` | Yes | Admin/display name. |
| `AppFileSync__Apps__0__MaxPlaintextBytes` | No | Per-file plaintext cap. Default is 5 MiB. |
| `AppFileSync__Apps__0__IsEnabled` | No | Disables app access when `false`. Default is `true`. |
| `Authentication__AppIdentity__ChallengeLifetimeSeconds` | No | Challenge lifetime. Default is `300`. |
| `Authentication__AppIdentity__SessionLifetimeSeconds` | No | AppFileSync bearer token lifetime. Default is `900`. |
| `Http__PathBase` | No | Route prefix when hosted behind a path, for example `/appfilesync`. |
| `Authentication__Authority` / `Authentication__Audience` | No | Optional OIDC compatibility mode. Leave empty for AppIdentity-only deployments. |

`OidcClientId` still exists in the app model for compatibility, but AppIdentity deployments can omit it. The app registration service stores `AppId` as the compatibility client id when `OidcClientId` is empty.

## App Registration

Example environment variables:

```text
AppFileSync__Apps__0__AppId=fifo-calculator
AppFileSync__Apps__0__DisplayName=FIFO Calculator
AppFileSync__Apps__0__MaxPlaintextBytes=5242880
AppFileSync__Apps__0__IsEnabled=true
```

Multiple apps use the normal ASP.NET Core array convention:

```text
AppFileSync__Apps__1__AppId=pokemon
AppFileSync__Apps__1__DisplayName=Pokemon
AppFileSync__Apps__1__MaxPlaintextBytes=5242880
AppFileSync__Apps__1__IsEnabled=true
```

The hosted service upserts configured apps on startup. It does not delete apps that are removed from configuration.

## Database Setup

Restore the local EF tool:

```bash
dotnet tool restore
```

Apply migrations to the configured database:

```bash
dotnet tool run dotnet-ef database update \
  --project src/AppFileSync.Api/AppFileSync.Api.csproj \
  --startup-project src/AppFileSync.Api/AppFileSync.Api.csproj
```

For first-run personal deployments, `AppFileSync__MigrateOnStartup=true` is supported. For repeatable production deployments, prefer applying migrations as a separate deployment step.

## Local Run

Start PostgreSQL however you prefer. A minimal local container is:

```bash
docker run --rm --name appfilesync-postgres \
  -e POSTGRES_USER=appfilesync \
  -e POSTGRES_PASSWORD=appfilesync \
  -e POSTGRES_DB=appfilesync \
  -p 5432:5432 \
  postgres:17-alpine
```

Run the API:

```bash
AppFileSync__MigrateOnStartup=true \
AppFileSync__Apps__0__AppId=fifo-calculator \
AppFileSync__Apps__0__DisplayName="FIFO Calculator" \
dotnet run --project src/AppFileSync.Api/AppFileSync.Api.csproj
```

Check health:

```bash
curl http://localhost:5000/healthz
curl http://localhost:5000/readyz
```

The exact local port depends on the ASP.NET launch profile. In containers and Kubernetes, the app listens on `8080`.

## Container Image

The repository includes a multi-stage `Dockerfile`:

```bash
docker build -t appfilesync-api:local .
```

Run it against a reachable PostgreSQL instance:

```bash
docker run --rm -p 8080:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=5432;Database=appfilesync;Username=appfilesync;Password=appfilesync" \
  -e AppFileSync__MigrateOnStartup=true \
  -e AppFileSync__Apps__0__AppId=fifo-calculator \
  -e AppFileSync__Apps__0__DisplayName="FIFO Calculator" \
  appfilesync-api:local
```

Health endpoint:

```bash
curl http://localhost:8080/healthz
```

## Kubernetes Deployment

Generic manifests live in `deploy/kubernetes`:

```bash
kubectl apply -f deploy/kubernetes/namespace.yaml
kubectl apply -f deploy/kubernetes/postgres.yaml
kubectl apply -f deploy/kubernetes/api.yaml
```

Before applying `api.yaml`, create the API secret:

```bash
kubectl -n appfilesync create secret generic appfilesync-api-secrets \
  --from-literal='ConnectionStrings__Postgres=Host=appfilesync-postgres-rw;Port=5432;Database=appfilesync;Username=appfilesync;Password=...'
```

The generic `api.yaml` configures:

- `Authentication__AppIdentity__ChallengeLifetimeSeconds=300`
- `Authentication__AppIdentity__SessionLifetimeSeconds=900`
- one registered app, `fifo-calculator`
- readiness probe at `/readyz`
- liveness probe at `/healthz`

Replace:

- `ghcr.io/your-org/appfilesync-api:latest`
- `appfilesync.example.com`
- the cert-manager cluster issuer annotation if needed
- app registrations for your real apps

## Raspberry Pi 4 Overlay

The `deploy/kubernetes/rpi4` overlay is tailored for the existing Raspberry Pi cluster:

- Traefik ingress class.
- `letsencrypt-production` cluster issuer.
- `filesync.superjmn.com` host.
- `local-path` PostgreSQL storage.
- hostPath-published API binaries at `/home/jmn/appfilesync/publish`.

Publish API binaries for ARM64:

```bash
dotnet publish src/AppFileSync.Api/AppFileSync.Api.csproj \
  --configuration Release \
  --runtime linux-arm64 \
  --self-contained false \
  --output /tmp/appfilesync-rpi4-publish \
  /p:UseAppHost=false
```

Copy them to the Pi:

```bash
rsync -az --delete /tmp/appfilesync-rpi4-publish/ \
  jmn@192.168.1.29:/home/jmn/appfilesync/publish/
```

Apply the overlay:

```bash
kubectl apply -f deploy/kubernetes/rpi4/namespace.yaml
kubectl apply -f deploy/kubernetes/rpi4/postgres.yaml
kubectl apply -f deploy/kubernetes/rpi4/api-hostpath.yaml
```

Expected public health endpoint:

```text
https://filesync.superjmn.com/healthz
```

## Auth Flow At Runtime

Clients do not send passwords or API keys to sync files.

1. Client creates or imports an `AppIdentity`.
2. Client calls `POST /v1/auth/challenges` with `appId` and raw Ed25519 public key encoded as base64url.
3. Server returns a short-lived random challenge.
4. Client signs the challenge with the local private key.
5. Client calls `POST /v1/auth/sessions`.
6. Server verifies the signature and returns an `afs1.*` bearer token.
7. Client uses that bearer token on `/v1/apps/{appId}/...`.

The token contains:

```text
sub = ed25519:{publicKey}
azp = {appId}
```

Every data query filters by `owner_subject` and `app_id`.

## Operational Notes

- Keep the API behind HTTPS. AppIdentity proves possession of a private key, but bearer tokens still need transport protection.
- The current manifests run one API replica. If you scale to multiple replicas or want session tokens to survive restarts, configure a shared ASP.NET Core Data Protection key ring.
- Session tokens are intentionally short-lived. The default is 15 minutes.
- Challenge records are stored in process memory. A challenge must be answered by the same API instance that created it unless a distributed challenge store is added.
- PostgreSQL backups contain encrypted content, encrypted metadata, app registrations, devices, key envelopes, and file history. Protect backups anyway because they are still user data.
- The server does not currently enforce per-owner storage quotas or rate limits.
- File payloads are stored directly in PostgreSQL. The current intended maximum is small app files, defaulting to 5 MiB plaintext.

## Verification Checklist

After deployment:

```bash
curl https://your-host/healthz
curl https://your-host/readyz
```

Then verify with a real client identity:

1. Create/import an identity for a configured app.
2. Save a small JSON file.
3. Load the same logical path.
4. Export the identity.
5. Import it on another client.
6. Load the same logical path again.
7. Attempt a stale save and confirm `409 Conflict`.

The API test suite covers the implemented auth and isolation behavior:

```bash
dotnet test AppFileSync.slnx
```
