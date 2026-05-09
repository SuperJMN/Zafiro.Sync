namespace Zafiro.Sync.Client;

public interface IZafiroSyncClient
{
    Task<IReadOnlyList<RemoteFileDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    Task<SyncResult> SyncNowAsync(CancellationToken cancellationToken = default);

    Task<LoadFileResult> LoadAsync(string logicalPath, CancellationToken cancellationToken = default);

    Task<SaveFileResult> SaveAsync(
        string logicalPath,
        ReadOnlyMemory<byte> content,
        string? contentType = null,
        IReadOnlyDictionary<string, string>? tags = null,
        long? baseRevision = null,
        CancellationToken cancellationToken = default);

    Task<DeleteFileResult> DeleteAsync(
        string logicalPath,
        long baseRevision,
        CancellationToken cancellationToken = default);
}

public sealed record RemoteFileDescriptor(
    string FileId,
    long Revision,
    bool IsDeleted,
    int PlaintextSizeBytes,
    int CiphertextSizeBytes,
    string CipherHash,
    byte[] EncryptedMetadata);

public sealed record SyncResult(
    string PreviousCursor,
    string NextCursor,
    bool HasMore,
    IReadOnlyList<RemoteFileDescriptor> Changes);

public abstract record LoadFileResult
{
    public sealed record Found(string FileId, long Revision, FileMetadata Metadata, byte[] Content) : LoadFileResult;
    public sealed record NotFound : LoadFileResult;
}

public abstract record SaveFileResult
{
    public sealed record Saved(string FileId, long Revision, string Cursor) : SaveFileResult;
    public sealed record Conflict(string FileId, long CurrentRevision, string CurrentCursor, byte[] LocalContent) : SaveFileResult;
}

public abstract record DeleteFileResult
{
    public sealed record Deleted(string FileId, long Revision, string Cursor) : DeleteFileResult;
    public sealed record Conflict(string FileId, long CurrentRevision, string CurrentCursor) : DeleteFileResult;
}
