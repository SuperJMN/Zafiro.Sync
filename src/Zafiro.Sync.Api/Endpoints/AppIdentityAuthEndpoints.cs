using System.Security.Cryptography;
using System.Text;
using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NSec.Cryptography;

namespace Zafiro.Sync.Api.Endpoints;

public static partial class ZafiroSyncEndpoints
{
    private static readonly SignatureAlgorithm AppIdentitySignature = SignatureAlgorithm.Ed25519;

    private static async Task<IResult> CreateAppIdentityChallenge(
        [FromBody] CreateAppIdentityChallengeRequest request,
        [FromServices] ZafiroSyncDbContext db,
        [FromServices] AppIdentityChallengeStore challengeStore,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AppId) ||
            !TryNormalizePublicKey(request.PublicKey, out var publicKey))
        {
            return BadRequest();
        }

        var app = await db.Apps.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.AppId == request.AppId && candidate.IsEnabled, cancellationToken);

        if (app is null)
        {
            return NotFound();
        }

        var challenge = challengeStore.Create(app.AppId, publicKey);

        return Results.Ok(new AppIdentityChallengeResponse(
            challenge.ChallengeId,
            challenge.AppId,
            challenge.PublicKey,
            challenge.Challenge,
            challenge.ExpiresAt));
    }

    private static IResult CreateAppIdentitySession(
        [FromBody] CreateAppIdentitySessionRequest request,
        [FromServices] AppIdentityChallengeStore challengeStore,
        [FromServices] AppIdentityTokenService tokenService)
    {
        if (!challengeStore.TryConsume(request.ChallengeId, out var challenge) ||
            !TryNormalizePublicKey(request.PublicKey, out var publicKey) ||
            !PublicKeysEqual(challenge.PublicKey, publicKey) ||
            !TryDecodeSignature(request.Signature, out var signature) ||
            !SignatureIsValid(publicKey, challenge.Challenge, signature))
        {
            return Unauthorized();
        }

        var session = tokenService.Create(challenge.AppId, publicKey, request.DeviceId);

        return Results.Ok(new AppIdentitySessionResponse(session.AccessToken, session.ExpiresAt));
    }

    private static IResult Unauthorized()
    {
        return Results.Json(new ApiError("unauthenticated"), statusCode: StatusCodes.Status401Unauthorized);
    }

    private static bool TryNormalizePublicKey(string value, out string publicKey)
    {
        publicKey = "";

        try
        {
            var bytes = ApiBase64Url.Decode(value);
            if (bytes.Length != AppIdentitySignature.PublicKeySize)
            {
                return false;
            }

            _ = PublicKey.Import(AppIdentitySignature, bytes, KeyBlobFormat.RawPublicKey);
            publicKey = ApiBase64Url.Encode(bytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException)
        {
            return false;
        }
    }

    private static bool TryDecodeSignature(string value, out byte[] signature)
    {
        try
        {
            signature = ApiBase64Url.Decode(value);
            return signature.Length == AppIdentitySignature.SignatureSize;
        }
        catch (FormatException)
        {
            signature = [];
            return false;
        }
    }

    private static bool SignatureIsValid(string publicKey, string challenge, ReadOnlySpan<byte> signature)
    {
        try
        {
            var importedPublicKey = PublicKey.Import(AppIdentitySignature, ApiBase64Url.Decode(publicKey), KeyBlobFormat.RawPublicKey);
            return AppIdentitySignature.Verify(importedPublicKey, Encoding.UTF8.GetBytes(challenge), signature);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static bool PublicKeysEqual(string first, string second)
    {
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(first), Encoding.ASCII.GetBytes(second));
    }
}
