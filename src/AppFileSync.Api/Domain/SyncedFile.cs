namespace AppFileSync.Api.Domain;

public sealed class SyncedFile
{
    public Guid Id { get; set; }

    public string OwnerSubject { get; set; } = "";

    public string AppId { get; set; } = "";

    public string FileId { get; set; } = "";

    public long Revision { get; set; }

    public long ChangeSequence { get; set; }

    public byte[] EncryptedMetadata { get; set; } = [];

    public byte[] Ciphertext { get; set; } = [];

    public string CipherHash { get; set; } = "";

    public int PlaintextSizeBytes { get; set; }

    public int CiphertextSizeBytes { get; set; }

    public bool IsDeleted { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public List<FileVersion> Versions { get; set; } = [];
}
