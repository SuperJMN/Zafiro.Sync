# API Draft

All endpoints except health checks and AppIdentity session bootstrap require a valid bearer token.

The primary token is an AppFileSync session token created from a local Ed25519 identity. The API infers the owner from `sub = ed25519:{publicKey}`. Clients never send `userId`.

OIDC bearer tokens can still be accepted when configured, but they are not the default path.

## Health

### `GET /healthz`

Process liveness.

### `GET /readyz`

Checks PostgreSQL connectivity and migration readiness.

## Apps

### `GET /v1/apps`

Lists apps available to the current token.

Response:

```json
{
  "apps": [
    {
      "appId": "fifo-calculator",
      "displayName": "FIFO Calculator",
      "maxPlaintextBytes": 5242880
    }
  ]
}
```

## AppIdentity Auth

### `POST /v1/auth/challenges`

Creates a short-lived challenge for a registered app and Ed25519 public key.

Request:

```json
{
  "appId": "fifo-calculator",
  "publicKey": "base64url-raw-ed25519-public-key"
}
```

Response:

```json
{
  "challengeId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7",
  "appId": "fifo-calculator",
  "publicKey": "base64url-raw-ed25519-public-key",
  "challenge": "base64url-random-challenge",
  "expiresAt": "2026-05-08T12:00:00Z"
}
```

### `POST /v1/auth/sessions`

Verifies possession of the private key and returns a short AppFileSync bearer token.

Request:

```json
{
  "challengeId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7",
  "publicKey": "base64url-raw-ed25519-public-key",
  "signature": "base64url-ed25519-signature-over-challenge",
  "deviceId": "9f4a4d8f-e6cc-4fd7-b6b4-8f8a5c6ddf40"
}
```

Response:

```json
{
  "accessToken": "afs1....",
  "expiresAt": "2026-05-08T12:15:00Z",
  "tokenType": "Bearer"
}
```

## Change Feed

### `GET /v1/apps/{appId}/changes?after={cursor}&limit={limit}`

Returns file changes for the authenticated user and app.

Response:

```json
{
  "nextCursor": "184467",
  "hasMore": false,
  "changes": [
    {
      "fileId": "Z1ciV77zPXqLQKxWcEoyv7MYPkZrJ5eB",
      "revision": 4,
      "isDeleted": false,
      "plaintextSizeBytes": 918,
      "ciphertextSizeBytes": 1012,
      "cipherHash": "sha256:...",
      "encryptedMetadata": "base64..."
    }
  ]
}
```

## Files

### `GET /v1/apps/{appId}/files/{fileId}`

Downloads encrypted metadata and encrypted content for a file.

Response:

```json
{
  "fileId": "Z1ciV77zPXqLQKxWcEoyv7MYPkZrJ5eB",
  "revision": 4,
  "encryptedMetadata": "base64...",
  "ciphertext": "base64...",
  "cipherHash": "sha256:...",
  "plaintextSizeBytes": 918,
  "ciphertextSizeBytes": 1012,
  "updatedAt": "2026-05-08T12:00:00Z"
}
```

### `PUT /v1/apps/{appId}/files/{fileId}`

Creates or updates a file.

Request:

```json
{
  "baseRevision": 3,
  "deviceId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7",
  "encryptedMetadata": "base64...",
  "ciphertext": "base64...",
  "cipherHash": "sha256:...",
  "plaintextSizeBytes": 918
}
```

Response:

```json
{
  "revision": 4,
  "cursor": "184467",
  "updatedAt": "2026-05-08T12:00:00Z"
}
```

Conflict response:

```json
{
  "error": "conflict",
  "currentRevision": 4,
  "currentCursor": "184467"
}
```

### `DELETE /v1/apps/{appId}/files/{fileId}`

Creates a delete tombstone. The request still includes `baseRevision`.

Request:

```json
{
  "baseRevision": 4,
  "deviceId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7"
}
```

Response:

```json
{
  "revision": 5,
  "cursor": "184468",
  "deleted": true
}
```

## Devices

### `GET /v1/apps/{appId}/devices`

Lists active and revoked devices for the authenticated user/app.

### `POST /v1/apps/{appId}/devices`

Registers the current device.

Request:

```json
{
  "deviceId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7",
  "displayName": "Framework Laptop",
  "publicKey": "base64..."
}
```

## Key Envelopes

### `GET /v1/apps/{appId}/key-envelopes`

Lists encrypted app-key envelopes for this user/app.

### `POST /v1/apps/{appId}/key-envelopes`

Uploads an encrypted app-key envelope for a target device.

Request:

```json
{
  "deviceId": "1a35b5fb-8a6e-43bf-aed4-f180ad041ed7",
  "envelopeVersion": 1,
  "encryptedAppKey": "base64..."
}
```

## Error Codes

| HTTP | Code | Meaning |
| --- | --- | --- |
| 400 | `invalid_request` | Malformed request |
| 401 | `unauthenticated` | Missing or invalid token |
| 403 | `forbidden_app` | Token is not valid for this app |
| 404 | `not_found` | File/app not found or not visible |
| 409 | `conflict` | Base revision is stale |
| 413 | `file_too_large` | File exceeds app limit |
| 429 | `rate_limited` | Too many requests |
