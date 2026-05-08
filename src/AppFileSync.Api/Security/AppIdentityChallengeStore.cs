using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace AppFileSync.Api.Security;

public sealed class AppIdentityChallengeStore(
    IMemoryCache memoryCache,
    IOptions<AppIdentityAuthenticationOptions> options)
{
    public AppIdentityChallenge Create(string appId, string publicKey)
    {
        var lifetime = TimeSpan.FromSeconds(Math.Max(1, options.Value.ChallengeLifetimeSeconds));
        var challenge = new AppIdentityChallenge(
            Guid.NewGuid(),
            appId,
            publicKey,
            ApiBase64Url.Encode(RandomNumberGenerator.GetBytes(32)),
            DateTimeOffset.UtcNow.Add(lifetime));

        memoryCache.Set(CacheKey(challenge.ChallengeId), challenge, challenge.ExpiresAt);
        return challenge;
    }

    public bool TryConsume(Guid challengeId, out AppIdentityChallenge challenge)
    {
        if (!memoryCache.TryGetValue(CacheKey(challengeId), out challenge!))
        {
            return false;
        }

        memoryCache.Remove(CacheKey(challengeId));

        return challenge.ExpiresAt > DateTimeOffset.UtcNow;
    }

    private static string CacheKey(Guid challengeId)
    {
        return $"app-identity-challenge:{challengeId:N}";
    }
}

public sealed record AppIdentityChallenge(
    Guid ChallengeId,
    string AppId,
    string PublicKey,
    string Challenge,
    DateTimeOffset ExpiresAt);
