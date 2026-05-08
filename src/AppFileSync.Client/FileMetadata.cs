namespace AppFileSync.Client;

public sealed record FileMetadata(
    string LogicalPath,
    string? ContentType,
    IReadOnlyDictionary<string, string> Tags);

public sealed record FileEncryptionContext(
    string AppId,
    string FileId,
    long? RevisionIntent);

public sealed record EncryptedFilePayload(
    byte[] EncryptedMetadata,
    byte[] Ciphertext,
    string CipherHash,
    int PlaintextSizeBytes,
    int CiphertextSizeBytes);

public sealed record DecryptedFilePayload(
    FileMetadata Metadata,
    byte[] Content);

public interface IFileEncryptor
{
    EncryptedFilePayload Encrypt(FileMetadata metadata, ReadOnlySpan<byte> content, FileEncryptionContext context);

    DecryptedFilePayload Decrypt(EncryptedFilePayload payload, FileEncryptionContext context);
}
