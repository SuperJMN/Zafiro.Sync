using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using NSec.Cryptography;

namespace Zafiro.Sync.Client.Tests;

public sealed class AppIdentityTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Create_ShouldGenerateEd25519SubjectAndAppDataKey()
    {
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
        var message = Encoding.UTF8.GetBytes("challenge");
        var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, Decode(identity.PublicKey), KeyBlobFormat.RawPublicKey);

        var signature = identity.Sign(message);

        identity.AppId.Should().Be("fifo-calculator");
        identity.DisplayName.Should().Be("FIFO Calculator");
        identity.AppDataKey.Should().HaveCount(32);
        identity.Subject.Should().Be($"ed25519:{identity.PublicKey}");
        SignatureAlgorithm.Ed25519.Verify(publicKey, message, signature).Should().BeTrue();
    }

    [Fact]
    public void ExportThenImport_WithCorrectPassword_ShouldRestoreTheSameIdentity()
    {
        var identity = AppIdentity.Create("pokemon", "Pokemon");

        var exported = identity.Export("correct horse battery staple");
        var restored = AppIdentity.Import("correct horse battery staple", exported);

        restored.AppId.Should().Be(identity.AppId);
        restored.DisplayName.Should().Be(identity.DisplayName);
        restored.DeviceId.Should().Be(identity.DeviceId);
        restored.PublicKey.Should().Be(identity.PublicKey);
        restored.Subject.Should().Be(identity.Subject);
        restored.AppDataKey.Should().Equal(identity.AppDataKey);
    }

    [Fact]
    public void Import_WithWrongPassword_ShouldFailWithoutReturningPartialIdentity()
    {
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
        var exported = identity.Export("correct-password");

        var act = () => AppIdentity.Import("wrong-password", exported);

        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public async Task TokenProvider_ShouldSignChallengeAndReturnSessionToken()
    {
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
        var challengeId = Guid.NewGuid();
        const string challenge = "challenge-from-server";
        var requests = new List<string>();
        var handler = new StubHandler(async request =>
        {
            requests.Add(request.RequestUri!.AbsolutePath);

            if (request.RequestUri.AbsolutePath == "/v1/auth/challenges")
            {
                var body = await request.Content!.ReadFromJsonAsync<ChallengeRequest>(JsonOptions);
                body.Should().BeEquivalentTo(new
                {
                    AppId = identity.AppId,
                    PublicKey = identity.PublicKey,
                });

                return JsonResponse(new ChallengeResponse(
                    challengeId,
                    identity.AppId,
                    identity.PublicKey,
                    challenge,
                    DateTimeOffset.UtcNow.AddMinutes(5)));
            }

            if (request.RequestUri.AbsolutePath == "/v1/auth/sessions")
            {
                var body = await request.Content!.ReadFromJsonAsync<SessionRequest>(JsonOptions);
                body.Should().NotBeNull();
                body!.ChallengeId.Should().Be(challengeId);
                body.PublicKey.Should().Be(identity.PublicKey);
                body.DeviceId.Should().Be(identity.DeviceId);

                var publicKey = PublicKey.Import(SignatureAlgorithm.Ed25519, Decode(body.PublicKey), KeyBlobFormat.RawPublicKey);
                var signed = Encoding.UTF8.GetBytes(challenge);
                SignatureAlgorithm.Ed25519.Verify(publicKey, signed, Decode(body.Signature)).Should().BeTrue();

                return JsonResponse(new SessionResponse("session-token", DateTimeOffset.UtcNow.AddMinutes(15)));
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
        var provider = new AppIdentityTokenProvider(
            new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") },
            new Uri("https://sync.test"),
            identity);

        var token = await provider.GetAccessTokenAsync();

        token.Should().Be("session-token");
        requests.Should().Equal("/v1/auth/challenges", "/v1/auth/sessions");
    }

    private static HttpResponseMessage JsonResponse<T>(T value)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value, options: JsonOptions),
        };
    }

    private static byte[] Decode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return handle(request);
        }
    }

    private sealed record ChallengeRequest(string AppId, string PublicKey);
    private sealed record ChallengeResponse(Guid ChallengeId, string AppId, string PublicKey, string Challenge, DateTimeOffset ExpiresAt);
    private sealed record SessionRequest(Guid ChallengeId, string PublicKey, string Signature, Guid DeviceId);
    private sealed record SessionResponse(string AccessToken, DateTimeOffset ExpiresAt);
}
