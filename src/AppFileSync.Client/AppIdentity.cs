using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace AppFileSync.Client;

public sealed class AppIdentity
{
    private const int AppDataKeySize = 32;
    private const int ExportKeySize = 32;
    private const int ExportNonceSize = 12;
    private const int ExportTagSize = 16;
    private const int ExportSaltSize = 16;
    private const int Argon2MemorySize = 19 * 1024;
    private const int Argon2Passes = 2;
    private const int Argon2Parallelism = 1;
    private static readonly SignatureAlgorithm Signature = SignatureAlgorithm.Ed25519;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly byte[] privateKey;
    private readonly byte[] appDataKey;

    private AppIdentity(
        string appId,
        string displayName,
        Guid deviceId,
        string publicKey,
        byte[] privateKey,
        byte[] appDataKey,
        DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (appDataKey.Length < AppDataKeySize)
        {
            throw new ArgumentException("The app data key must contain at least 32 bytes.", nameof(appDataKey));
        }

        AppId = appId;
        DisplayName = displayName;
        DeviceId = deviceId;
        PublicKey = publicKey;
        this.privateKey = privateKey.ToArray();
        this.appDataKey = appDataKey[..AppDataKeySize].ToArray();
        CreatedAt = createdAt;
    }

    public string AppId { get; }

    public string DisplayName { get; }

    public Guid DeviceId { get; }

    public string PublicKey { get; }

    public string Subject => $"ed25519:{PublicKey}";

    public byte[] AppDataKey => appDataKey.ToArray();

    public DateTimeOffset CreatedAt { get; }

    public static AppIdentity Create(string appId, string displayName)
    {
        using var key = Key.Create(Signature, ExportableKeyParameters());
        var privateKey = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicKey = Base64Url.Encode(key.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        return new AppIdentity(
            appId,
            displayName,
            Guid.NewGuid(),
            publicKey,
            privateKey,
            RandomNumberGenerator.GetBytes(AppDataKeySize),
            DateTimeOffset.UtcNow);
    }

    public static AppIdentity Import(string password, ReadOnlySpan<byte> bytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var export = JsonSerializer.Deserialize<AppIdentityExport>(bytes, JsonOptions)
            ?? throw new CryptographicException("The identity export is empty.");

        if (export.Version != 1 || export.Kdf.Algorithm != "argon2id")
        {
            throw new CryptographicException("Unsupported identity export format.");
        }

        var key = DeriveExportKey(password, export.Kdf);
        var privateKey = DecryptSecret(export.EncryptedPrivateKey, key, CreateAssociatedData(export, "private-key"));
        var appDataKey = DecryptSecret(export.EncryptedAppDataKey, key, CreateAssociatedData(export, "app-data-key"));
        using var signingKey = Key.Import(Signature, privateKey, KeyBlobFormat.RawPrivateKey, ExportableKeyParameters());
        var publicKey = Base64Url.Encode(signingKey.PublicKey.Export(KeyBlobFormat.RawPublicKey));

        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(publicKey), Encoding.ASCII.GetBytes(export.IdentityPublicKey)))
        {
            throw new CryptographicException("The identity export public key does not match the private key.");
        }

        return new AppIdentity(
            export.AppId,
            export.DisplayName,
            export.DeviceId,
            export.IdentityPublicKey,
            privateKey,
            appDataKey,
            export.CreatedAt);
    }

    public byte[] Export(string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var kdf = new IdentityExportKdf(
            "argon2id",
            Base64Url.Encode(RandomNumberGenerator.GetBytes(ExportSaltSize)),
            Argon2MemorySize,
            Argon2Passes,
            Argon2Parallelism);
        var key = DeriveExportKey(password, kdf);
        var export = new AppIdentityExport(
            1,
            AppId,
            DisplayName,
            PublicKey,
            DeviceId,
            EncryptSecret(privateKey, key, CreateAssociatedData(AppId, PublicKey, DeviceId, "private-key")),
            EncryptSecret(appDataKey, key, CreateAssociatedData(AppId, PublicKey, DeviceId, "app-data-key")),
            kdf,
            CreatedAt);

        return JsonSerializer.SerializeToUtf8Bytes(export, JsonOptions);
    }

    public byte[] Sign(ReadOnlySpan<byte> message)
    {
        using var key = Key.Import(Signature, privateKey, KeyBlobFormat.RawPrivateKey, ExportableKeyParameters());
        return Signature.Sign(key, message);
    }

    private static KeyCreationParameters ExportableKeyParameters()
    {
        return new KeyCreationParameters
        {
            ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
        };
    }

    private static byte[] DeriveExportKey(string password, IdentityExportKdf kdf)
    {
        var algorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(new Argon2Parameters
        {
            DegreeOfParallelism = kdf.DegreeOfParallelism,
            MemorySize = kdf.MemorySize,
            NumberOfPasses = kdf.NumberOfPasses,
        });
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        return algorithm.DeriveBytes(passwordBytes, Base64Url.Decode(kdf.Salt), ExportKeySize);
    }

    private static EncryptedSecret EncryptSecret(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, byte[] associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(ExportNonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[ExportTagSize];

        using var aes = new AesGcm(key, ExportTagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);

        return new EncryptedSecret(
            "A256GCM",
            Base64Url.Encode(nonce),
            Base64Url.Encode(ciphertext),
            Base64Url.Encode(tag));
    }

    private static byte[] DecryptSecret(EncryptedSecret secret, ReadOnlySpan<byte> key, byte[] associatedData)
    {
        if (secret.Algorithm != "A256GCM")
        {
            throw new CryptographicException("Unsupported encrypted identity secret format.");
        }

        var nonce = Base64Url.Decode(secret.Nonce);
        var ciphertext = Base64Url.Decode(secret.Ciphertext);
        var tag = Base64Url.Decode(secret.Tag);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, ExportTagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext, associatedData);

        return plaintext;
    }

    private static byte[] CreateAssociatedData(AppIdentityExport export, string purpose)
    {
        return CreateAssociatedData(export.AppId, export.IdentityPublicKey, export.DeviceId, purpose);
    }

    private static byte[] CreateAssociatedData(string appId, string publicKey, Guid deviceId, string purpose)
    {
        return Encoding.UTF8.GetBytes($"AppFileSync.IdentityExport.v1:{purpose}:{appId}:{publicKey}:{deviceId}");
    }

    private sealed record AppIdentityExport(
        int Version,
        string AppId,
        string DisplayName,
        string IdentityPublicKey,
        Guid DeviceId,
        EncryptedSecret EncryptedPrivateKey,
        EncryptedSecret EncryptedAppDataKey,
        IdentityExportKdf Kdf,
        DateTimeOffset CreatedAt);

    private sealed record EncryptedSecret(
        string Algorithm,
        string Nonce,
        string Ciphertext,
        string Tag);

    private sealed record IdentityExportKdf(
        string Algorithm,
        string Salt,
        int MemorySize,
        int NumberOfPasses,
        int DegreeOfParallelism);
}
