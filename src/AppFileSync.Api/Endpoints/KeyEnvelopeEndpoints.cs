using AppFileSync.Api.Data;
using AppFileSync.Api.Domain;
using AppFileSync.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppFileSync.Api.Endpoints;

public static partial class AppFileSyncEndpoints
{
    private static async Task<IResult> GetKeyEnvelopes(
        [FromRoute] string appId,
        HttpContext httpContext,
        [FromServices] AppFileSyncDbContext db,
        [FromServices] AppAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var auth = await authorization.AuthorizeAsync(httpContext.User, appId, cancellationToken);
        if (!auth.IsAllowed)
        {
            return Error(auth);
        }

        var envelopes = await db.KeyEnvelopes.AsNoTracking()
            .Where(envelope => envelope.OwnerSubject == auth.OwnerSubject && envelope.AppId == appId)
            .OrderBy(envelope => envelope.DeviceId)
            .ThenBy(envelope => envelope.EnvelopeVersion)
            .Select(envelope => new KeyEnvelopeResponse(
                envelope.DeviceId,
                envelope.EnvelopeVersion,
                Convert.ToBase64String(envelope.EncryptedAppKey),
                envelope.CreatedAt))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new KeyEnvelopesResponse(envelopes));
    }

    private static async Task<IResult> AddKeyEnvelope(
        [FromRoute] string appId,
        [FromBody] AddKeyEnvelopeRequest request,
        HttpContext httpContext,
        [FromServices] AppFileSyncDbContext db,
        [FromServices] AppAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var auth = await authorization.AuthorizeAsync(httpContext.User, appId, cancellationToken);
        if (!auth.IsAllowed)
        {
            return Error(auth);
        }

        if (!TryDecode(request.EncryptedAppKey, out var encryptedAppKey))
        {
            return BadRequest();
        }

        var envelope = new KeyEnvelope
        {
            Id = Guid.NewGuid(),
            OwnerSubject = auth.OwnerSubject!,
            AppId = appId,
            DeviceId = request.DeviceId,
            EnvelopeVersion = request.EnvelopeVersion,
            EncryptedAppKey = encryptedAppKey,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.KeyEnvelopes.Add(envelope);
        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new KeyEnvelopeResponse(
            envelope.DeviceId,
            envelope.EnvelopeVersion,
            Convert.ToBase64String(envelope.EncryptedAppKey),
            envelope.CreatedAt));
    }
}
