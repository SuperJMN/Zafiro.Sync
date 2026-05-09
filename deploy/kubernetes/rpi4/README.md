# RPi4 Deployment

This overlay matches the current Raspberry Pi Kubernetes cluster:

- ingress class: `traefik`
- cluster issuer: `letsencrypt-production`
- storage class: `local-path`
- public host: `filesync.superjmn.com`
- route prefix: `/`

It avoids a custom application image. The API runs from a hostPath published directory at `/home/jmn/zafiro-sync/publish` using `mcr.microsoft.com/dotnet/aspnet:10.0`.

## Publish App

From the repo root:

```bash
dotnet publish src/Zafiro.Sync.Api/Zafiro.Sync.Api.csproj \
  --configuration Release \
  --runtime linux-arm64 \
  --self-contained false \
  --output /tmp/zafiro-sync-rpi4-publish \
  /p:UseAppHost=false

rsync -az --delete /tmp/zafiro-sync-rpi4-publish/ \
  jmn@192.168.1.29:/home/jmn/zafiro-sync/publish/
```

## Apply

Create database secrets out of band, including `ConnectionStrings__Postgres`, then apply the manifests. cert-manager issues the `filesync.superjmn.com` TLS certificate from the `letsencrypt-production` cluster issuer.

```bash
kubectl apply -f namespace.yaml
kubectl apply -f postgres.yaml
kubectl apply -f api-hostpath.yaml
```

The API is expected at:

```text
https://filesync.superjmn.com/healthz
```
