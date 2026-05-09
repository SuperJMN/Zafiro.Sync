# Kubernetes Deployment

These manifests are a production-shaped starting point, not a complete secret bundle.

See [Server Deployment](../../docs/server-deployment.md) for the full runtime and deployment notes.

## Prerequisites

- CloudNativePG installed in the cluster.
- Ingress controller and cert-manager installed.
- Secrets managed outside plain git, for example SOPS/age, Sealed Secrets, or external-secrets.

## Required Secrets

Create these secrets before applying `api.yaml`:

```text
zafiro-sync-api-secrets
  ConnectionStrings__Postgres

zafiro-sync-postgres-superuser
  username
  password

zafiro-sync-postgres-app
  username
  password
```

`ConnectionStrings__Postgres` should point at the CloudNativePG read-write service.

## Apply

```bash
kubectl apply -f namespace.yaml
kubectl apply -f postgres.yaml
kubectl apply -f api.yaml
```

Run `dotnet tool run dotnet-ef database update` against the target connection string, or set `ZafiroSync__MigrateOnStartup=true` for a controlled first deployment.
