using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Zafiro.Sync.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Zafiro.Sync.Api.Endpoints;

public static partial class ZafiroSyncEndpoints
{
    private static async Task<IResult> GetDevices(
        [FromRoute] string appId,
        HttpContext httpContext,
        [FromServices] ZafiroSyncDbContext db,
        [FromServices] AppAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var auth = await authorization.AuthorizeAsync(httpContext.User, appId, cancellationToken);
        if (!auth.IsAllowed)
        {
            return Error(auth);
        }

        var devices = await db.Devices.AsNoTracking()
            .Where(device => device.OwnerSubject == auth.OwnerSubject && device.AppId == appId)
            .OrderBy(device => device.DisplayName)
            .ThenBy(device => device.Id)
            .Select(device => new DeviceResponse(
                device.Id,
                device.DisplayName,
                Convert.ToBase64String(device.PublicKey),
                device.CreatedAt,
                device.RevokedAt))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new DevicesResponse(devices));
    }

    private static async Task<IResult> RegisterDevice(
        [FromRoute] string appId,
        [FromBody] RegisterDeviceRequest request,
        HttpContext httpContext,
        [FromServices] ZafiroSyncDbContext db,
        [FromServices] AppAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        var auth = await authorization.AuthorizeAsync(httpContext.User, appId, cancellationToken);
        if (!auth.IsAllowed)
        {
            return Error(auth);
        }

        if (!TryDecode(request.PublicKey, out var publicKey) || string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest();
        }

        var existing = await db.Devices.FirstOrDefaultAsync(device =>
            device.OwnerSubject == auth.OwnerSubject &&
            device.AppId == appId &&
            device.Id == request.DeviceId,
            cancellationToken);

        if (existing is null)
        {
            existing = new DeviceRegistration
            {
                Id = request.DeviceId,
                OwnerSubject = auth.OwnerSubject!,
                AppId = appId,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Devices.Add(existing);
        }

        existing.DisplayName = request.DisplayName;
        existing.PublicKey = publicKey;
        existing.RevokedAt = null;

        await db.SaveChangesAsync(cancellationToken);

        return Results.Ok(new DeviceResponse(
            existing.Id,
            existing.DisplayName,
            Convert.ToBase64String(existing.PublicKey),
            existing.CreatedAt,
            existing.RevokedAt));
    }
}
