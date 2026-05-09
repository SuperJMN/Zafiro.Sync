using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Zafiro.Sync.Client;

public sealed class FakeFileEncryptor : IFileEncryptor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public EncryptedFilePayload Encrypt(FileMetadata metadata, ReadOnlySpan<byte> content, FileEncryptionContext context)
    {
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(metadata with
        {
            LogicalPath = LogicalPath.Normalize(metadata.LogicalPath),
        }, JsonOptions);
        var contentBytes = content.ToArray();

        return new EncryptedFilePayload(
            metadataBytes,
            contentBytes,
            CreateHash(contentBytes),
            content.Length,
            content.Length);
    }

    public DecryptedFilePayload Decrypt(EncryptedFilePayload payload, FileEncryptionContext context)
    {
        var metadata = JsonSerializer.Deserialize<FileMetadata>(payload.EncryptedMetadata, JsonOptions)
            ?? throw new InvalidOperationException("The fake metadata payload is empty.");

        return new DecryptedFilePayload(metadata, payload.Ciphertext);
    }

    private static string CreateHash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return $"sha256:{Base64Url.Encode(hash)}";
    }
}
