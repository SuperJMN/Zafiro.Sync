using System.Security.Cryptography;
using System.Text;
using FluentAssertions;

namespace AppFileSync.Client.Tests;

public sealed class AesGcmFileEncryptorTests
{
    [Fact]
    public void EncryptAndDecrypt_ShouldRoundTripWithoutLeakingPlaintext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encryptor = new AesGcmFileEncryptor(key);
        var metadata = new FileMetadata("settings/profile.json", "application/json", new Dictionary<string, string>
        {
            ["kind"] = "settings",
        });
        var context = new FileEncryptionContext("fifo-calculator", "file-1", 7);
        var content = Encoding.UTF8.GetBytes("{\"theme\":\"dark\"}");

        var encrypted = encryptor.Encrypt(metadata, content, context);

        Encoding.UTF8.GetString(encrypted.EncryptedMetadata).Should().NotContain("settings/profile.json");
        Encoding.UTF8.GetString(encrypted.Ciphertext).Should().NotContain("dark");
        encrypted.CipherHash.Should().StartWith("sha256:");

        var decrypted = encryptor.Decrypt(encrypted, context);
        decrypted.Metadata.Should().BeEquivalentTo(metadata);
        decrypted.Content.Should().Equal(content);
    }

    [Fact]
    public void Decrypt_WhenAssociatedDataChanges_ShouldFail()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var encryptor = new AesGcmFileEncryptor(key);
        var metadata = new FileMetadata("settings.json", "application/json", new Dictionary<string, string>());
        var content = Encoding.UTF8.GetBytes("{}");
        var encrypted = encryptor.Encrypt(metadata, content, new FileEncryptionContext("app-a", "file-1", 1));

        var act = () => encryptor.Decrypt(encrypted, new FileEncryptionContext("app-b", "file-1", 1));

        act.Should().Throw<CryptographicException>();
    }
}
