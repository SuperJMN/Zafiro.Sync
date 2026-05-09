namespace Zafiro.Sync.Api.Domain;

public sealed class FileVersion
{
    public Guid Id { get; set; }

    public Guid SyncedFileId { get; set; }

    public SyncedFile SyncedFile { get; set; } = null!;

    public long Revision { get; set; }

    public byte[] EncryptedMetadata { get; set; } = [];

    public byte[] Ciphertext { get; set; } = [];

    public string CipherHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public Guid? CreatedByDeviceId { get; set; }
}
