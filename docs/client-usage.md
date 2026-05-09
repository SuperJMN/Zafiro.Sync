# Client Usage

This document describes the Zafiro.Sync client shape that is implemented today in `Zafiro.Sync.Client`.

The SDK gives an app:

- a local Ed25519 identity per app;
- a random 32-byte app data key;
- password-protected identity export/import;
- challenge/session authentication against the API;
- AES-GCM encryption for metadata and file content;
- revision-based `Load`, `Save`, `Delete`, `List`, and `SyncNow`.

The server never receives a user id, plaintext file path, or plaintext file content.

## Requirements

- .NET 9 or later for `Zafiro.Sync.Client`.
- A registered server-side `appId`.
- A writable local place for the identity export and sync cursor.

`NSec.Cryptography 26.4.0` is used by the SDK for Ed25519 identity keys and Argon2id password derivation.

## Identity Model

Each app should create its own identity by default:

```text
fifo-calculator -> ed25519 public key A + app data key A
pokemon         -> ed25519 public key B + app data key B
```

The API sees the owner as:

```text
owner_subject = ed25519:{publicKey}
app_id        = {appId}
file_id       = HMAC(appDataKey, normalizedLogicalPath)
```

Because `file_id` is derived from the app data key, importing the same identity on a second device restores access to the same files.

## Minimal Integration

```csharp
using System.Text;
using Zafiro.Sync.Client;

var identity = await identityStore.LoadOrCreate("fifo-calculator", "FIFO Calculator");
var httpClient = new HttpClient
{
    BaseAddress = new Uri("https://filesync.superjmn.com"),
};

var sync = new ZafiroSyncClient(
    httpClient,
    new ZafiroSyncClientOptions
    {
        ServiceBaseUri = new Uri("https://filesync.superjmn.com"),
        AppId = identity.AppId,
        DeviceId = identity.DeviceId,
        AppDataKey = identity.AppDataKey,
    },
    new AppIdentityTokenProvider(httpClient, new Uri("https://filesync.superjmn.com"), identity),
    new AesGcmFileEncryptor(identity.AppDataKey),
    stateStore);

var saved = await sync.SaveAsync(
    "settings/preferences.json",
    Encoding.UTF8.GetBytes("""{ "theme": "dark" }"""),
    "application/json",
    baseRevision: previousRevision);
```

The example assumes the app owns `identityStore` and `stateStore`. Zafiro.Sync provides `InMemoryZafiroSyncStateStore` for tests and demos, but real apps should persist cursors and revisions.

One minimal file-backed identity store looks like this:

```csharp
public sealed class FileIdentityStore(string path, string password)
{
    public async Task<AppIdentity> LoadOrCreate(string appId, string displayName)
    {
        if (File.Exists(path))
        {
            var bytes = await File.ReadAllBytesAsync(path);
            return AppIdentity.Import(password, bytes);
        }

        var identity = AppIdentity.Create(appId, displayName);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
        await File.WriteAllBytesAsync(path, identity.Export(password));
        return identity;
    }
}
```

For a production app, replace the hard-coded password with user input, platform secure storage, or a recovery flow that matches the app's threat model.

## Local State To Keep

The app must persist:

| Value | Why |
| --- | --- |
| Identity export | Restores Ed25519 private key, public key, device id, and app data key. |
| Identity export password policy | The SDK requires a password for export/import. The app decides how users provide or store it. |
| Last known file revision per logical path | Required for optimistic concurrency on `SaveAsync` and `DeleteAsync`. |
| Change cursor per app | Required by `SyncNowAsync` to pull changes incrementally. |
| Local dirty bytes | Needed when a save returns conflict. |

Do not store `AppDataKey` as a separate plaintext setting unless the host platform has a secure storage mechanism and the app explicitly chooses that trade-off. The portable baseline is the encrypted identity export.

## Creating And Persisting An Identity

```csharp
var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
var exportBytes = identity.Export(password);

await File.WriteAllBytesAsync("fifo-calculator.identity.json", exportBytes);
```

Loading it later:

```csharp
var exportBytes = await File.ReadAllBytesAsync("fifo-calculator.identity.json");
var identity = AppIdentity.Import(password, exportBytes);
```

Wrong passwords throw `CryptographicException`; they do not return a partial identity.

## Export And Import Between Devices

Export:

```csharp
var exportBytes = identity.Export(password);
await File.WriteAllBytesAsync("zafiro-sync-fifo-calculator-identity.json", exportBytes);
```

Import:

```csharp
var identity = AppIdentity.Import(password, exportBytes);
```

The imported identity keeps the same:

- `AppId`
- `DeviceId`
- `PublicKey`
- `Subject`
- `AppDataKey`

That means the second device reads and writes the same remote owner/app namespace and derives the same opaque file ids.

## Saving Files

```csharp
var result = await sync.SaveAsync(
    logicalPath: "database.json",
    content: jsonBytes,
    contentType: "application/json",
    tags: new Dictionary<string, string> { ["kind"] = "catalog" },
    baseRevision: lastKnownRevision);

switch (result)
{
    case SaveFileResult.Saved saved:
        await revisionStore.Save("database.json", saved.Revision);
        await cursorStore.Save(saved.Cursor);
        break;

    case SaveFileResult.Conflict conflict:
        await pendingWrites.KeepLocalBytes("database.json", conflict.LocalContent);
        await revisionStore.Save("database.json", conflict.CurrentRevision);
        break;
}
```

Use `baseRevision: null` only when creating a file that the client has never observed. For later saves, pass the last remote revision that the app loaded or saved.

## Loading Files

```csharp
var result = await sync.LoadAsync("database.json");

switch (result)
{
    case LoadFileResult.Found found:
        await localCache.Write(found.Metadata.LogicalPath, found.Content);
        await revisionStore.Save(found.Metadata.LogicalPath, found.Revision);
        break;

    case LoadFileResult.NotFound:
        await localCache.MarkMissing("database.json");
        break;
}
```

`LoadAsync` computes the opaque `file_id` locally from the logical path and app data key. The server does not see `database.json`.

## Pulling Changes

```csharp
var syncResult = await sync.SyncNowAsync();

foreach (var change in syncResult.Changes)
{
    if (change.IsDeleted)
    {
        await localCache.DeleteByFileId(change.FileId);
        continue;
    }

    // The descriptor includes encrypted metadata only.
    // Call LoadAsync for logical paths already known by the app, or keep
    // an app-level index file if the app needs discovery by file id.
}
```

The current SDK returns change descriptors and persists the cursor through `IZafiroSyncStateStore`. It does not yet provide a full local file cache or automatic conflict resolver.

## Deleting Files

```csharp
var result = await sync.DeleteAsync("settings/preferences.json", baseRevision);

switch (result)
{
    case DeleteFileResult.Deleted deleted:
        await revisionStore.Save("settings/preferences.json", deleted.Revision);
        await cursorStore.Save(deleted.Cursor);
        break;

    case DeleteFileResult.Conflict conflict:
        await revisionStore.Save("settings/preferences.json", conflict.CurrentRevision);
        break;
}
```

Deletes create tombstones on the server so other devices can observe the deletion through the change feed.

## Demo App

`src/Zafiro.Sync.Demo` is the executable reference for the current flow. It implements:

- local identity creation;
- identity export/import;
- identity storage at `DemoClient:IdentityFilePath`;
- `settings/demo.json` load/save through `AppIdentityTokenProvider`;
- conflict display for stale revisions.

Configuration:

```json
{
  "DemoClient": {
    "ServiceBaseUri": "https://filesync.superjmn.com",
    "AppId": "fifo-calculator",
    "LogicalPath": "settings/demo.json",
    "IdentityFilePath": "settings/demo.identity.json",
    "LocalIdentityPassword": "zafiro-sync-demo-local"
  }
}
```

`LocalIdentityPassword` is acceptable for a local demo only. A real app should ask the user, use platform secure storage, or implement an explicit recovery flow.

## Implemented Limits

- Files are whole-document saves; there is no merge engine.
- Conflicts return `409` and the SDK exposes conflict result objects.
- Server-side file size enforcement uses each app registration's `MaxPlaintextBytes`.
- The SDK encrypts metadata and content, but the server still stores ciphertext in PostgreSQL rather than object storage.
- Device and key-envelope endpoints exist, but the current identity import/export flow does not require them for second-device onboarding.
