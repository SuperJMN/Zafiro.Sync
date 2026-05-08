using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AppFileSync.Client;

public sealed class AesGcmFileEncryptor : IFileEncryptor
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly byte[] appDataKey;

    public AesGcmFileEncryptor(ReadOnlySpan<byte> appDataKey)
    {
        if (appDataKey.Length < KeySize)
        {
            throw new ArgumentException("The app data key must contain at least 32 bytes.", nameof(appDataKey));
        }

        this.appDataKey = appDataKey[..KeySize].ToArray();
    }

    public EncryptedFilePayload Encrypt(FileMetadata metadata, ReadOnlySpan<byte> content, FileEncryptionContext context)
    {
        var normalizedMetadata = metadata with { LogicalPath = LogicalPath.Normalize(metadata.LogicalPath) };
        var metadataBytes = JsonSerializer.SerializeToUtf8Bytes(normalizedMetadata, JsonOptions);
        var encryptedMetadata = EncryptEnvelope(metadataBytes, context, "metadata");
        var encryptedContent = EncryptEnvelope(content, context, "content");

        return new EncryptedFilePayload(
            encryptedMetadata,
            encryptedContent,
            CreateCipherHash(encryptedContent),
            content.Length,
            encryptedContent.Length);
    }

    public DecryptedFilePayload Decrypt(EncryptedFilePayload payload, FileEncryptionContext context)
    {
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(payload.CipherHash),
                Encoding.ASCII.GetBytes(CreateCipherHash(payload.Ciphertext))))
        {
            throw new CryptographicException("The ciphertext hash does not match the payload.");
        }

        var metadataBytes = DecryptEnvelope(payload.EncryptedMetadata, context, "metadata");
        var content = DecryptEnvelope(payload.Ciphertext, context, "content");
        var metadata = JsonSerializer.Deserialize<FileMetadata>(metadataBytes, JsonOptions)
            ?? throw new CryptographicException("The metadata envelope is empty.");

        return new DecryptedFilePayload(metadata, content);
    }

    private byte[] EncryptEnvelope(ReadOnlySpan<byte> plaintext, FileEncryptionContext context, string purpose)
    {
        var key = DeriveKey(context, purpose);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var associatedData = CreateAssociatedData(context, purpose);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        var envelope = new EncryptionEnvelope(
            Version: 1,
            Algorithm: "A256GCM",
            Nonce: Convert.ToBase64String(nonce),
            Ciphertext: Convert.ToBase64String(ciphertext),
            Tag: Convert.ToBase64String(tag));

        return JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
    }

    private byte[] DecryptEnvelope(ReadOnlySpan<byte> envelopeBytes, FileEncryptionContext context, string purpose)
    {
        var envelope = JsonSerializer.Deserialize<EncryptionEnvelope>(envelopeBytes, JsonOptions)
            ?? throw new CryptographicException("The encrypted envelope is empty.");

        if (envelope is not { Version: 1, Algorithm: "A256GCM" })
        {
            throw new CryptographicException("Unsupported encrypted envelope format.");
        }

        var nonce = Convert.FromBase64String(envelope.Nonce);
        var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
        var tag = Convert.FromBase64String(envelope.Tag);
        var plaintext = new byte[ciphertext.Length];
        var key = DeriveKey(context, purpose);
        var associatedData = CreateAssociatedData(context, purpose);

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    private byte[] DeriveKey(FileEncryptionContext context, string purpose)
    {
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes($"{context.AppId}\n{context.FileId}"));
        using var extract = new HMACSHA256(salt);
        var pseudoRandomKey = extract.ComputeHash(appDataKey);
        var info = Encoding.UTF8.GetBytes($"AppFileSync:{purpose}:v1");
        var input = new byte[info.Length + 1];
        Buffer.BlockCopy(info, 0, input, 0, info.Length);
        input[^1] = 1;

        using var expand = new HMACSHA256(pseudoRandomKey);
        return expand.ComputeHash(input)[..KeySize];
    }

    private static byte[] CreateAssociatedData(FileEncryptionContext context, string purpose)
    {
        return Encoding.UTF8.GetBytes($"AppFileSync:v1:{purpose}:{context.AppId}:{context.FileId}:{context.RevisionIntent}");
    }

    private static string CreateCipherHash(ReadOnlySpan<byte> ciphertext)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(ciphertext, hash);
        return $"sha256:{Base64Url.Encode(hash)}";
    }

    private sealed record EncryptionEnvelope(
        int Version,
        string Algorithm,
        string Nonce,
        string Ciphertext,
        string Tag);
}
