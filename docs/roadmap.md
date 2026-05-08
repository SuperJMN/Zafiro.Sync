# Implementation Roadmap

## Phase 1: Prove the Backend Contract

Create a minimal API and database where payloads are already treated as opaque ciphertext.

Tasks:

- Create solution and projects:
  - `src/AppFileSync.Api`
  - `src/AppFileSync.Client`
  - `tests/AppFileSync.Api.Tests`
  - `tests/AppFileSync.Client.Tests`
- Add PostgreSQL migrations.
- Add `apps`, `files`, `file_versions`, `devices`, and `key_envelopes`.
- Implement AppIdentity challenge/session validation.
- Enforce app isolation from the session `azp` app id.
- Implement upload, download, delete, and changes feed.
- Add integration tests with SQLite first and Testcontainers PostgreSQL later.

Exit criteria:

- User isolation test passes.
- App isolation test passes.
- 5 MiB file upload/download round-trips.
- Stale write returns `409 Conflict`.

## Phase 2: Client SDK Without Real Crypto

Use fake encryption first so sync behavior can be tested independently.

Tasks:

- Define `IAppFileSyncClient`.
- Add local metadata cache.
- Add file cache abstraction.
- Implement pull/push.
- Implement conflict result model.
- Add local app identity creation, import/export, and token provider.
- Add deterministic tests for offline edits and reconnect.

Exit criteria:

- SDK syncs many files for one app.
- SDK handles 0-file app state.
- SDK returns a conflict without losing local bytes.

## Phase 3: Real Client-Side Encryption

Replace fake encryption with versioned encrypted envelopes.

Tasks:

- Use `NSec.Cryptography` for Ed25519 and Argon2id.
- Define envelope format:
  - version
  - algorithm
  - nonce
  - associated data
  - ciphertext
- Encrypt metadata and content.
- Generate app data key on first run.
- Add recovery-key onboarding.
- Add device registration and key envelope flow.

Exit criteria:

- Database contains no plaintext file content.
- Database contains no plaintext logical paths.
- New device can decrypt after recovery-key onboarding.

## Phase 4: Kubernetes Deployment

Deploy the service on a personal cluster.

Tasks:

- Add container build.
- Add Kubernetes manifests or Helm chart.
- Deploy PostgreSQL through CloudNativePG.
- Add ingress and TLS.
- Add secret workflow with SOPS/age or Sealed Secrets.
- Add backup and restore scripts/runbook.

Exit criteria:

- Fresh cluster deployment works from Git plus secrets.
- Restore drill works into a clean namespace.
- API is reachable over HTTPS.

## Phase 5: FIFOCalculator Pilot

Use FIFOCalculator as the first real consumer.

Tasks:

- Add AppFileSync client package reference.
- Implement sync-backed entry catalog repository.
- Keep local JSON storage as cache and offline fallback.
- Add login/sync status UI.
- Add manual sync command.
- Add conservative conflict UI.

Exit criteria:

- `database.json` syncs between two devices.
- Offline edit syncs after reconnect.
- Conflicting edits do not silently overwrite fiscal data.

## Phase 6: Hardening

Tasks:

- Rate limits.
- Quotas per user/app.
- Version retention cleanup job.
- Device revocation.
- Metrics.
- Structured audit events without payload data.
- Admin runbook.

Exit criteria:

- Abuse limits are enforced.
- Old file versions are cleaned.
- Revoked devices cannot receive new key envelopes.
- Operational dashboard shows health, errors, storage use, and sync volume.
