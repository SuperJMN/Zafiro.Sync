using System.Security.Cryptography;
using System.Text;

namespace AppFileSync.Client;

public static class FileIdGenerator
{
    public static string CreateFileId(ReadOnlySpan<byte> appDataKey, string logicalPath)
    {
        if (appDataKey.Length < 32)
        {
            throw new ArgumentException("The app data key must contain at least 32 bytes.", nameof(appDataKey));
        }

        var normalizedPath = LogicalPath.Normalize(logicalPath);
        var pathBytes = Encoding.UTF8.GetBytes(normalizedPath);
        Span<byte> hash = stackalloc byte[32];

        using var hmac = new HMACSHA256(appDataKey.ToArray());
        if (!hmac.TryComputeHash(pathBytes, hash, out var written) || written != hash.Length)
        {
            throw new CryptographicException("Unable to compute the file id.");
        }

        return Base64Url.Encode(hash);
    }
}
