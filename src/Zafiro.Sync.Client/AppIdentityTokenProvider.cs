using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Zafiro.Sync.Client;

public sealed class AppIdentityTokenProvider(
    HttpClient httpClient,
    Uri serviceBaseUri,
    AppIdentity identity)
    : IZafiroSyncTokenProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private string? accessToken;
    private DateTimeOffset expiresAt;

    public async ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (accessToken is not null && expiresAt > DateTimeOffset.UtcNow.AddSeconds(30))
        {
            return accessToken;
        }

        httpClient.BaseAddress ??= serviceBaseUri;
        var challengeResponse = await httpClient.PostAsJsonAsync(
            "/v1/auth/challenges",
            new ChallengeRequest(identity.AppId, identity.PublicKey),
            JsonOptions,
            cancellationToken);
        challengeResponse.EnsureSuccessStatusCode();
        var challenge = await challengeResponse.Content.ReadFromJsonAsync<ChallengeResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The challenge response was empty.");

        if (challenge.AppId != identity.AppId || challenge.PublicKey != identity.PublicKey)
        {
            throw new InvalidOperationException("The challenge response does not match the local identity.");
        }

        var signature = identity.Sign(Encoding.UTF8.GetBytes(challenge.Challenge));
        var sessionResponse = await httpClient.PostAsJsonAsync(
            "/v1/auth/sessions",
            new SessionRequest(challenge.ChallengeId, identity.PublicKey, Base64Url.Encode(signature), identity.DeviceId),
            JsonOptions,
            cancellationToken);
        sessionResponse.EnsureSuccessStatusCode();
        var session = await sessionResponse.Content.ReadFromJsonAsync<SessionResponse>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The session response was empty.");

        accessToken = session.AccessToken;
        expiresAt = session.ExpiresAt;

        return accessToken;
    }

    private sealed record ChallengeRequest(string AppId, string PublicKey);
    private sealed record ChallengeResponse(Guid ChallengeId, string AppId, string PublicKey, string Challenge, DateTimeOffset ExpiresAt);
    private sealed record SessionRequest(Guid ChallengeId, string PublicKey, string Signature, Guid DeviceId);
    private sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt);
}
