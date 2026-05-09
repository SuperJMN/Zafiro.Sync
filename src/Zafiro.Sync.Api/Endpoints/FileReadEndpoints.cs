using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Zafiro.Sync.Api.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Zafiro.Sync.Api.Endpoints;

public static partial class ZafiroSyncEndpoints
{
    private static async Task<IResult> GetChanges(
        [FromRoute] string appId,
        [FromQuery] long after,
        [FromQuery] int? limit,
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

        var take = Math.Clamp(limit ?? 100, 1, 500);
        var rows = await db.Files.AsNoTracking()
            .Where(file => file.OwnerSubject == auth.OwnerSubject && file.AppId == appId && file.ChangeSequence > after)
            .OrderBy(file => file.ChangeSequence)
            .Take(take + 1)
            .ToArrayAsync(cancellationToken);
        var hasMore = rows.Length > take;
        var visibleRows = rows.Take(take).ToArray();
        var nextCursor = visibleRows.Length == 0 ? after : visibleRows[^1].ChangeSequence;
        var changes = visibleRows.Select(ToChangeResponse).ToArray();

        return Results.Ok(new ChangesResponse(nextCursor.ToString(), hasMore, changes));
    }

    private static async Task<IResult> GetFile(
        [FromRoute] string appId,
        [FromRoute] string fileId,
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

        var file = await db.Files.AsNoTracking()
            .FirstOrDefaultAsync(file =>
                file.OwnerSubject == auth.OwnerSubject &&
                file.AppId == appId &&
                file.FileId == fileId &&
                !file.IsDeleted,
                cancellationToken);

        return file is null ? NotFound() : Results.Ok(ToFileResponse(file));
    }

    private static ChangeResponse ToChangeResponse(SyncedFile file)
    {
        return new ChangeResponse(
            file.FileId,
            file.Revision,
            file.IsDeleted,
            file.PlaintextSizeBytes,
            file.CiphertextSizeBytes,
            file.CipherHash,
            Convert.ToBase64String(file.EncryptedMetadata));
    }

    private static FileResponse ToFileResponse(SyncedFile file)
    {
        return new FileResponse(
            file.FileId,
            file.Revision,
            Convert.ToBase64String(file.EncryptedMetadata),
            Convert.ToBase64String(file.Ciphertext),
            file.CipherHash,
            file.PlaintextSizeBytes,
            file.CiphertextSizeBytes,
            file.UpdatedAt);
    }
}
