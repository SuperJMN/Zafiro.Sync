# Service Plan

## Problem

Several apps need a boring way to sync small user-owned files between devices. Each app should be able to say "save this file for this user" without owning a custom backend.

FIFOCalculator is the reference consumer:

```text
app_id = fifo-calculator
files:
  database.json
  settings.json
```

The service must also work for future apps with 0..n files per user/app.

## Scope

### In Scope

- Users represented by local app identities based on Ed25519 key pairs.
- Optional OpenID Connect compatibility for deployments that still need it.
- Multiple apps registered in the service.
- 0..n files per user/app.
- Max plaintext file size: 5 MiB.
- File listing, upload, download, delete, and incremental change pull.
- Offline-friendly optimistic concurrency.
- Client-side encryption for content and metadata.
- Kubernetes self-hosting.
- Backup and restore plan.

### Out of Scope

- Realtime collaborative editing.
- Arbitrary SQL exposed to apps.
- Large media storage or CDN behavior.
- Full SQLite replication.
- Server-side plaintext indexing or search.
- Cross-user sharing in v1.

## Product Model

### User / Identity

A local app identity identified by `sub = ed25519:{publicKey}`. The service never trusts a user id sent in the request body and does not manage emails, passwords, or account profiles.

Each app normally owns its own identity and app data key. That keeps `fifo-calculator` and `pokemon` from being linkable by the same public key unless the user deliberately imports the same identity into both apps.

### App

An app is a registered namespace such as `fifo-calculator`, `proyecto-ana`, or `secure-vault`.

Apps are part of authorization. A token issued for app A must not be able to write app B files. AppIdentity tokens set `azp` to the route `appId`; OIDC compatibility tokens can still match an allowed audience/client id.

### File

A file is a named logical document inside one user/app namespace.

The server should not receive the raw path by default. The client SDK turns the logical key into an opaque stable id:

```text
file_id = base64url(HMAC(app_data_key, normalized_logical_path))
```

The human path, content type, and app metadata go into encrypted metadata.

## Architecture

```text
Avalonia/MAUI/WPF/web app
  Zafiro.Sync.Client
    - local cache
    - encryption
    - conflict handling
        |
        | HTTPS + AppIdentity bearer token
        v
Zafiro.Sync.Api
  - Ed25519 challenge/session validation
  - app authorization
  - user/app/file ownership
  - revision checks
  - encrypted payload storage
        |
        v
PostgreSQL
  - apps
  - files
  - file versions
  - device registrations
  - encrypted key envelopes
```

## Kubernetes Topology

Minimum production-like topology:

| Component | Purpose |
| --- | --- |
| `zafiro-sync-api` | ASP.NET Core sync API |
| PostgreSQL | Metadata and encrypted file bytes |
| ingress-nginx or Traefik | Public HTTPS |
| cert-manager | TLS certificates |
| SOPS/age or Sealed Secrets | Secret manifests without plaintext |
| backup job/operator | PostgreSQL backups and restore drills |

Object storage is optional in v1. PostgreSQL is simpler while files are capped at 5 MiB. Add S3/MinIO later behind an `IFileContentStore` interface if payload volume grows.

## Data Model

### `apps`

Registered applications.

| Column | Type | Notes |
| --- | --- | --- |
| `app_id` | text PK | Stable public app id |
| `oidc_client_id` | text | Optional compatibility JWT client/authorized party |
| `display_name` | text | Admin-visible |
| `max_plaintext_bytes` | int | Default 5 MiB |
| `is_enabled` | boolean | Disable app without deleting data |
| `created_at` | timestamptz | Server timestamp |

### `files`

Current state for each user/app/file.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | uuid PK | Internal id |
| `owner_subject` | text | `ed25519:{publicKey}` or compatibility OIDC `sub` |
| `app_id` | text FK | App namespace |
| `file_id` | text | Opaque stable id from SDK |
| `revision` | bigint | Monotonic file revision |
| `change_sequence` | bigint | Monotonic user/app feed cursor |
| `encrypted_metadata` | bytea | Path, content type, app tags |
| `ciphertext` | bytea | Encrypted file content |
| `cipher_hash` | text | Hash of ciphertext |
| `plaintext_size_bytes` | int | Claimed plaintext size, capped |
| `ciphertext_size_bytes` | int | Actual stored bytes |
| `is_deleted` | boolean | Tombstone |
| `created_at` | timestamptz | Server timestamp |
| `updated_at` | timestamptz | Server timestamp |

Unique index:

```text
(owner_subject, app_id, file_id)
```

### `file_versions`

Short retention history for conflict recovery.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | uuid PK | Internal id |
| `file_id` | uuid FK | Internal file id |
| `revision` | bigint | File revision |
| `encrypted_metadata` | bytea | Version metadata |
| `ciphertext` | bytea | Version content |
| `cipher_hash` | text | Hash of ciphertext |
| `created_at` | timestamptz | Server timestamp |
| `created_by_device_id` | uuid | Nullable |

Default retention: latest 10 versions or 30 days.

### `devices`

Known devices for one user/app pair.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | uuid PK | Device id generated by SDK |
| `owner_subject` | text | `ed25519:{publicKey}` or compatibility OIDC `sub` |
| `app_id` | text | App namespace |
| `display_name` | text | User-visible |
| `public_key` | bytea | Device public key |
| `created_at` | timestamptz | Server timestamp |
| `revoked_at` | timestamptz | Stops future sync/key sharing |

### `key_envelopes`

Encrypted app data keys for each authorized device.

| Column | Type | Notes |
| --- | --- | --- |
| `id` | uuid PK | Envelope id |
| `owner_subject` | text | `ed25519:{publicKey}` or compatibility OIDC `sub` |
| `app_id` | text | App namespace |
| `device_id` | uuid | Target device |
| `envelope_version` | int | Crypto format |
| `encrypted_app_key` | bytea | Server cannot decrypt |
| `created_at` | timestamptz | Server timestamp |

## Sync Semantics

Each file has its own revision. Each user/app namespace also has a change feed cursor.

### Pull

The client stores the last seen cursor per app. It asks for changes since that cursor.

The server returns changed file ids, revisions, tombstone state, encrypted metadata, and hashes. The client downloads content for changed non-deleted files.

### Push

The client uploads a file with:

- `baseRevision`: the revision it last observed, or `null` for create.
- encrypted metadata.
- encrypted content.
- content hash.
- device id.

The server accepts the write only if the current revision equals `baseRevision`.

### Conflict

If the current revision differs from `baseRevision`, the API returns `409 Conflict`.

Default SDK behavior:

- keep local unsynced data;
- download remote metadata/content;
- surface a conflict object to the app.

Suggested app policies:

| Data type | Default policy |
| --- | --- |
| UI settings | Last write wins is acceptable |
| Small app database | Ask user, keep both, or app-defined merge |
| Fiscal/accounting data | Never silent overwrite |

## Encryption Plan

### Goals

- The API and PostgreSQL must not contain plaintext user files.
- File paths can be hidden from the server.
- App A cannot decrypt app B files.
- Revoking a device stops future access, while accepting that already-decrypted local data cannot be erased remotely.

### App Data Key

Each user/app pair has a random app data key generated by the first device.

```text
user + fifo-calculator -> app_data_key_A
user + another-app      -> app_data_key_B
```

The app data key is stored only on devices and inside encrypted key envelopes.

### Identity Export

The SDK exports a portable identity JSON with:

- `version`
- `appId`
- `identityPublicKey`
- `deviceId`
- `encryptedPrivateKey`
- `encryptedAppDataKey`
- `kdf`
- `createdAt`

The export uses Argon2id to derive a password key and AES-GCM to encrypt the Ed25519 private key and app data key.

### File Encryption

For each save:

- Generate a fresh random nonce.
- Encrypt the full file with AEAD.
- Encrypt metadata separately or as part of the same envelope.
- Include associated data:
  - crypto version
  - app id
  - file id
  - revision intent

The exact primitive should come from a mature library. Preferred choices:

- XChaCha20-Poly1305 through libsodium-compatible bindings.
- AES-256-GCM if nonce generation and platform support are reliable.

### New Device Flow

V1 supports portable identity import/export:

1. First device creates an app identity and app data key.
2. User exports the identity JSON with a password.
3. New device imports the JSON with that password.
4. New device receives the same `owner_subject` and `app_data_key`.
5. The same opaque `file_id` values resolve and existing files can be decrypted.

Later, add QR pairing or device-specific key envelopes if sharing an identity file becomes too clumsy.

## Security Requirements

- No admin/service credentials in client apps.
- API validates Ed25519 challenge signatures before issuing short session tokens.
- AppIdentity session tokens expire quickly and carry `sub` plus `azp`.
- Route `appId` must match the authorized app id.
- Every query filters by authenticated `owner_subject`.
- Max plaintext size is enforced before storing.
- Max request body size is enforced at ingress and API.
- Logs never include tokens, identity export passwords, ciphertext bodies, or encrypted key envelopes.
- Database backups are encrypted.
- Kubernetes Secrets are encrypted at rest and not committed as plaintext.
- Rate limits protect login-facing and write endpoints.
- Admin UIs are behind VPN, private network, or strong access controls.

## First Implementation Milestones

### Milestone 1: Backend Skeleton

- ASP.NET Core API project.
- PostgreSQL migrations.
- AppIdentity challenge/session endpoints.
- `apps` table and app authorization.
- Health/readiness endpoints.

### Milestone 2: File CRUD and Revisions

- Upload encrypted file.
- Download encrypted file.
- Delete tombstone.
- List changed files by cursor.
- Optimistic concurrency with `409 Conflict`.
- Integration tests for user and app isolation.

### Milestone 3: C# Client SDK

- AppIdentity creation, export/import, and token provider.
- Local cache abstraction.
- Encryption abstraction.
- `List`, `Load`, `Save`, `Delete`, `SyncNow`.
- Conflict result model.

### Milestone 4: Client-Side Encryption

- App data key generation.
- Encrypted metadata.
- Encrypted content.
- Recovery-key onboarding.
- Device registration and key envelopes.
- Tests proving the stored database does not contain plaintext paths or file content.

### Milestone 5: Kubernetes Deployment

- Container image.
- Kubernetes manifests or Helm chart.
- CloudNativePG PostgreSQL cluster.
- Ingress + TLS.
- Secrets workflow.
- Backup and restore drill.

### Milestone 6: FIFOCalculator Pilot

- Implement a sync-backed repository for `database.json`.
- Keep local JSON as offline cache.
- Add identity/sync status to settings.
- Handle conflicts conservatively.

## Acceptance Criteria

- One user can sync 0, 1, and many files under one app.
- One file up to 5 MiB plaintext can be saved and restored exactly.
- User A cannot see or modify User B files.
- App A token cannot see or modify App B files for the same user.
- Server-side database does not contain plaintext file content or logical paths.
- Stale writes return `409 Conflict`.
- Tombstones propagate to a second device.
- Offline writes sync after reconnect.
- Backups can be restored into a clean namespace.
