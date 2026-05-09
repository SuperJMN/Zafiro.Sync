using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Zafiro.Sync.Api.Endpoints;

public static partial class ZafiroSyncEndpoints
{
    public static void MapZafiroSyncEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));
        app.MapGet("/readyz", async (ZafiroSyncDbContext db, CancellationToken cancellationToken) =>
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });

        var auth = app.MapGroup("/v1/auth");
        auth.MapPost("/challenges", CreateAppIdentityChallenge);
        auth.MapPost("/sessions", CreateAppIdentitySession);

        var api = app.MapGroup("/v1/apps").RequireAuthorization();

        api.MapGet("/", ListApps);
        api.MapGet("/{appId}/changes", GetChanges);
        api.MapGet("/{appId}/files/{fileId}", GetFile);
        api.MapPut("/{appId}/files/{fileId}", PutFile);
        api.MapDelete("/{appId}/files/{fileId}", DeleteFile);
        api.MapGet("/{appId}/devices", GetDevices);
        api.MapPost("/{appId}/devices", RegisterDevice);
        api.MapGet("/{appId}/key-envelopes", GetKeyEnvelopes);
        api.MapPost("/{appId}/key-envelopes", AddKeyEnvelope);
    }

    private static IResult Error(AppAuthorizationResult auth)
    {
        return Results.Json(auth.Error, statusCode: auth.StatusCode);
    }

    private static IResult BadRequest()
    {
        return Results.Json(new ApiError("invalid_request"), statusCode: StatusCodes.Status400BadRequest);
    }

    private static IResult NotFound()
    {
        return Results.Json(new ApiError("not_found"), statusCode: StatusCodes.Status404NotFound);
    }

    private static bool TryDecode(string base64, out byte[] bytes)
    {
        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }
}
