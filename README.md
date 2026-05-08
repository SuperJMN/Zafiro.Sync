# AppFileSync

Self-hosted synchronization service for small and medium encrypted app files.

## Goal

AppFileSync is a narrow, self-hosted sync backend for applications that need to keep a small set of user files synchronized across devices.

The target shape is:

```text
user
  app 0..n
    file 0..n, max 5 MiB each
```

It is designed for data like settings, small JSON databases, app state snapshots, and user documents where a full database replication engine would be excessive.

## Design Principles

- One API that works for many apps.
- Apps are isolated namespaces, not just filename prefixes.
- Apps authenticate through a local Ed25519 identity; OIDC can remain as an optional compatibility mode.
- Files are encrypted client-side before upload.
- The server stores opaque file ids, encrypted metadata, and encrypted content.
- Sync is revision-based and offline-friendly.
- Conflicts are detected, never silently overwritten by default.
- Kubernetes deployment stays small: API + PostgreSQL.

## Documentation

- [Service Plan](docs/service-plan.md)
- [API Draft](docs/api-draft.md)
- [Client Usage](docs/client-usage.md)
- [Server Deployment](docs/server-deployment.md)
- [Implementation Roadmap](docs/roadmap.md)
- [Kubernetes Deployment](deploy/kubernetes/README.md)

## Initial Stack

- ASP.NET Core API
- PostgreSQL, preferably through CloudNativePG in Kubernetes
- C# client SDK
- Ed25519 challenge/session authentication with `NSec.Cryptography`
- Client-side AEAD encryption

The first version intentionally stores encrypted file payloads in PostgreSQL. With a hard 5 MiB file limit, this keeps the service simpler than adding an object store from day one. An S3/MinIO storage adapter can be added later if real usage requires it.

## Development

Build and test:

```bash
dotnet test AppFileSync.slnx
```

Create or update the PostgreSQL schema:

```bash
dotnet tool restore
dotnet tool run dotnet-ef database update \
  --project src/AppFileSync.Api/AppFileSync.Api.csproj \
  --startup-project src/AppFileSync.Api/AppFileSync.Api.csproj
```

Run the API with configuration supplied through `appsettings`, environment variables, Kubernetes config, or user secrets:

```text
ConnectionStrings__Postgres
Authentication__Authority
Authentication__Audience
Authentication__AppIdentity__ChallengeLifetimeSeconds
Authentication__AppIdentity__SessionLifetimeSeconds
AppFileSync__Apps__0__AppId
AppFileSync__Apps__0__DisplayName
```

Set `AppFileSync__MigrateOnStartup=true` only for controlled deployments where the API process is allowed to apply migrations.

The primary auth flow is:

```text
POST /v1/auth/challenges -> sign challenge with local Ed25519 key
POST /v1/auth/sessions   -> receive short AppFileSync bearer token
```

The token maps to `sub = ed25519:{publicKey}` and `azp = {appId}`, so the existing app and owner isolation model is preserved.
