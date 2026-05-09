using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Extensions;
using Microsoft.Extensions.Options;

namespace Zafiro.Sync.Api.Security;

public sealed class AppIdentityTokenService(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<AppIdentityAuthenticationOptions> options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITimeLimitedDataProtector protector = dataProtectionProvider
        .CreateProtector("Zafiro.Sync.AppIdentity.SessionToken.v1")
        .ToTimeLimitedDataProtector();

    public AppIdentitySession Create(string appId, string publicKey, Guid deviceId)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Max(1, options.Value.SessionLifetimeSeconds));
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var payload = new AppIdentitySessionPayload(
            $"ed25519:{publicKey}",
            appId,
            deviceId,
            DateTimeOffset.UtcNow,
            expiresAt);
        var protectedPayload = protector.Protect(JsonSerializer.Serialize(payload, JsonOptions), lifetime);

        return new AppIdentitySession(
            $"{AppIdentityAuthenticationDefaults.TokenPrefix}{protectedPayload}",
            expiresAt);
    }

    public bool TryValidate(string token, out AppIdentitySessionPayload payload)
    {
        payload = default!;

        if (!token.StartsWith(AppIdentityAuthenticationDefaults.TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var protectedPayload = token[AppIdentityAuthenticationDefaults.TokenPrefix.Length..];
            var json = protector.Unprotect(protectedPayload);
            payload = JsonSerializer.Deserialize<AppIdentitySessionPayload>(json, JsonOptions)
                ?? throw new CryptographicException("The AppIdentity token payload is empty.");

            return payload.ExpiresAt > DateTimeOffset.UtcNow &&
                   !string.IsNullOrWhiteSpace(payload.Subject) &&
                   !string.IsNullOrWhiteSpace(payload.AppId);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException)
        {
            return false;
        }
    }
}

public sealed record AppIdentitySession(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record AppIdentitySessionPayload(
    string Subject,
    string AppId,
    Guid DeviceId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
