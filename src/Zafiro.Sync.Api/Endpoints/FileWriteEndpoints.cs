using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Zafiro.Sync.Api.Security;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Zafiro.Sync.Api.Endpoints;

public static partial class ZafiroSyncEndpoints
{
    private static async Task<IResult> PutFile(
        [FromRoute] string appId,
        [FromRoute] string fileId,
        [FromBody] PutFileRequest request,
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

        if (request.PlaintextSizeBytes > auth.App!.MaxPlaintextBytes)
        {
            return Results.Json(new ApiError("file_too_large"), statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!TryDecode(request.EncryptedMetadata, out var encryptedMetadata) ||
            !TryDecode(request.Ciphertext, out var ciphertext) ||
            string.IsNullOrWhiteSpace(request.CipherHash))
        {
            return BadRequest();
        }

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var file = await FindFileAsync(db, auth.OwnerSubject!, appId, fileId, cancellationToken);
        var currentRevision = file?.Revision;

        if (currentRevision != request.BaseRevision)
        {
            return Conflict(file);
        }

        if (file is null)
        {
            file = new SyncedFile
            {
                Id = Guid.NewGuid(),
                OwnerSubject = auth.OwnerSubject!,
                AppId = appId,
                FileId = fileId,
                CreatedAt = now,
            };
            db.Files.Add(file);
        }

        file.Revision += 1;
        file.ChangeSequence = await NextChangeSequenceAsync(db, auth.OwnerSubject!, appId, cancellationToken);
        file.EncryptedMetadata = encryptedMetadata;
        file.Ciphertext = ciphertext;
        file.CipherHash = request.CipherHash;
        file.PlaintextSizeBytes = request.PlaintextSizeBytes;
        file.CiphertextSizeBytes = ciphertext.Length;
        file.IsDeleted = false;
        file.UpdatedAt = now;
        db.FileVersions.Add(ToVersion(file, request.DeviceId, now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new WriteResponse(file.Revision, file.ChangeSequence.ToString(), file.UpdatedAt));
    }

    private static async Task<IResult> DeleteFile(
        [FromRoute] string appId,
        [FromRoute] string fileId,
        [FromBody] DeleteFileRequest request,
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

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var file = await FindFileAsync(db, auth.OwnerSubject!, appId, fileId, cancellationToken);

        if (file is null)
        {
            return NotFound();
        }

        if (file.Revision != request.BaseRevision)
        {
            return Conflict(file);
        }

        file.Revision += 1;
        file.ChangeSequence = await NextChangeSequenceAsync(db, auth.OwnerSubject!, appId, cancellationToken);
        file.IsDeleted = true;
        file.UpdatedAt = now;
        db.FileVersions.Add(ToVersion(file, request.DeviceId, now));

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return Results.Ok(new DeleteResponse(file.Revision, file.ChangeSequence.ToString(), true));
    }

    private static Task<SyncedFile?> FindFileAsync(
        ZafiroSyncDbContext db,
        string ownerSubject,
        string appId,
        string fileId,
        CancellationToken cancellationToken)
    {
        return db.Files.FirstOrDefaultAsync(existing =>
            existing.OwnerSubject == ownerSubject &&
            existing.AppId == appId &&
            existing.FileId == fileId,
            cancellationToken);
    }

    private static async Task<long> NextChangeSequenceAsync(
        ZafiroSyncDbContext db,
        string ownerSubject,
        string appId,
        CancellationToken cancellationToken)
    {
        var currentMax = await db.Files
            .Where(file => file.OwnerSubject == ownerSubject && file.AppId == appId)
            .MaxAsync(file => (long?)file.ChangeSequence, cancellationToken) ?? 0;

        return currentMax + 1;
    }

    private static FileVersion ToVersion(SyncedFile file, Guid? deviceId, DateTimeOffset now)
    {
        return new FileVersion
        {
            Id = Guid.NewGuid(),
            SyncedFileId = file.Id,
            Revision = file.Revision,
            EncryptedMetadata = file.EncryptedMetadata,
            Ciphertext = file.Ciphertext,
            CipherHash = file.CipherHash,
            CreatedAt = now,
            CreatedByDeviceId = deviceId,
        };
    }

    private static IResult Conflict(SyncedFile? file)
    {
        return Results.Json(
            new ApiError("conflict", file?.Revision, file?.ChangeSequence.ToString()),
            statusCode: StatusCodes.Status409Conflict);
    }
}
