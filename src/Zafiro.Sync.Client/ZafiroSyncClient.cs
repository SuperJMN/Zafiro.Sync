using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Zafiro.Sync.Client;

public sealed class ZafiroSyncClient(
    HttpClient httpClient,
    ZafiroSyncClientOptions options,
    IZafiroSyncTokenProvider tokenProvider,
    IFileEncryptor encryptor,
    IZafiroSyncStateStore? stateStore = null)
    : IZafiroSyncClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IZafiroSyncStateStore syncStateStore = stateStore ?? new InMemoryZafiroSyncStateStore();

    public async Task<IReadOnlyList<RemoteFileDescriptor>> ListAsync(CancellationToken cancellationToken = default)
    {
        var response = await PullChangesAsync("0", cancellationToken);
        return response.Changes.Select(ToDescriptor).ToArray();
    }

    public async Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        var previousCursor = await syncStateStore.LoadCursorAsync(options.AppId, cancellationToken) ?? "0";
        var response = await PullChangesAsync(previousCursor, cancellationToken);
        await syncStateStore.SaveCursorAsync(options.AppId, response.NextCursor, cancellationToken);

        return new SyncResult(
            previousCursor,
            response.NextCursor,
            response.HasMore,
            response.Changes.Select(ToDescriptor).ToArray());
    }

    private async Task<ChangesResponse> PullChangesAsync(string after, CancellationToken cancellationToken)
    {
        await AuthenticateAsync(cancellationToken);
        var response = await httpClient.GetFromJsonAsync<ChangesResponse>(
            $"/v1/apps/{Uri.EscapeDataString(options.AppId)}/changes?after={Uri.EscapeDataString(after)}",
            JsonOptions,
            cancellationToken);

        return response ?? new ChangesResponse(after, false, []);
    }

    private static RemoteFileDescriptor ToDescriptor(ChangeResponse change)
    {
        return new RemoteFileDescriptor(
            change.FileId,
            change.Revision,
            change.IsDeleted,
            change.PlaintextSizeBytes,
            change.CiphertextSizeBytes,
            change.CipherHash,
            Convert.FromBase64String(change.EncryptedMetadata));
    }

    public async Task<LoadFileResult> LoadAsync(string logicalPath, CancellationToken cancellationToken = default)
    {
        var fileId = FileIdGenerator.CreateFileId(options.AppDataKey, logicalPath);
        await AuthenticateAsync(cancellationToken);
        var response = await httpClient.GetAsync(FileUri(fileId), cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new LoadFileResult.NotFound();
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<FileResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The load response was empty.");
        var payload = new EncryptedFilePayload(
            Convert.FromBase64String(body.EncryptedMetadata),
            Convert.FromBase64String(body.Ciphertext),
            body.CipherHash,
            body.PlaintextSizeBytes,
            body.CiphertextSizeBytes);
        var decrypted = encryptor.Decrypt(payload, new FileEncryptionContext(options.AppId, fileId, body.Revision));

        return new LoadFileResult.Found(fileId, body.Revision, decrypted.Metadata, decrypted.Content);
    }

    public async Task<SaveFileResult> SaveAsync(
        string logicalPath,
        ReadOnlyMemory<byte> content,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? tags = null,
        long? baseRevision = null,
        CancellationToken cancellationToken = default)
    {
        var fileId = FileIdGenerator.CreateFileId(options.AppDataKey, logicalPath);
        var metadata = new FileMetadata(LogicalPath.Normalize(logicalPath), contentType, tags ?? new Dictionary<string, string>());
        var revisionIntent = baseRevision is null ? 1 : baseRevision.Value + 1;
        var localContent = content.ToArray();
        var encrypted = encryptor.Encrypt(metadata, localContent, new FileEncryptionContext(options.AppId, fileId, revisionIntent));
        var request = new PutFileRequest(
            baseRevision,
            options.DeviceId,
            Convert.ToBase64String(encrypted.EncryptedMetadata),
            Convert.ToBase64String(encrypted.Ciphertext),
            encrypted.CipherHash,
            encrypted.PlaintextSizeBytes);

        await AuthenticateAsync(cancellationToken);
        var response = await httpClient.PutAsJsonAsync(FileUri(fileId), request, JsonOptions, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The conflict response was empty.");
            return new SaveFileResult.Conflict(fileId, error.CurrentRevision ?? 0, error.CurrentCursor ?? "0", localContent);
        }

        response.EnsureSuccessStatusCode();
        var saved = await response.Content.ReadFromJsonAsync<WriteResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The save response was empty.");

        return new SaveFileResult.Saved(fileId, saved.Revision, saved.Cursor);
    }

    public async Task<DeleteFileResult> DeleteAsync(
        string logicalPath,
        long baseRevision,
        CancellationToken cancellationToken = default)
    {
        var fileId = FileIdGenerator.CreateFileId(options.AppDataKey, logicalPath);
        var request = new DeleteFileRequest(baseRevision, options.DeviceId);
        using var message = new HttpRequestMessage(HttpMethod.Delete, FileUri(fileId))
        {
            Content = JsonContent.Create(request, options: JsonOptions),
        };

        await AuthenticateAsync(cancellationToken);
        var response = await httpClient.SendAsync(message, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(JsonOptions, cancellationToken)
                ?? throw new InvalidOperationException("The conflict response was empty.");
            return new DeleteFileResult.Conflict(fileId, error.CurrentRevision ?? 0, error.CurrentCursor ?? "0");
        }

        response.EnsureSuccessStatusCode();
        var deleted = await response.Content.ReadFromJsonAsync<DeleteResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The delete response was empty.");

        return new DeleteFileResult.Deleted(fileId, deleted.Revision, deleted.Cursor);
    }

    private async Task AuthenticateAsync(CancellationToken cancellationToken)
    {
        var token = await tokenProvider.GetAccessTokenAsync(cancellationToken);
        httpClient.BaseAddress ??= options.ServiceBaseUri;
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private string FileUri(string fileId)
    {
        return $"/v1/apps/{Uri.EscapeDataString(options.AppId)}/files/{Uri.EscapeDataString(fileId)}";
    }

    private sealed record ChangesResponse(string NextCursor, bool HasMore, IReadOnlyList<ChangeResponse> Changes);
    private sealed record ChangeResponse(string FileId, long Revision, bool IsDeleted, int PlaintextSizeBytes, int CiphertextSizeBytes, string CipherHash, string EncryptedMetadata);
    private sealed record FileResponse(string FileId, long Revision, string EncryptedMetadata, string Ciphertext, string CipherHash, int PlaintextSizeBytes, int CiphertextSizeBytes, DateTimeOffset UpdatedAt);
    private sealed record PutFileRequest(long? BaseRevision, Guid DeviceId, string EncryptedMetadata, string Ciphertext, string CipherHash, int PlaintextSizeBytes);
    private sealed record DeleteFileRequest(long BaseRevision, Guid DeviceId);
    private sealed record WriteResponse(long Revision, string Cursor, DateTimeOffset UpdatedAt);
    private sealed record DeleteResponse(long Revision, string Cursor, bool Deleted);
    private sealed record ApiError(string Error, long? CurrentRevision = null, string? CurrentCursor = null);
}
