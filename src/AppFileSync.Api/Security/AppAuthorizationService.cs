using System.Security.Claims;
using AppFileSync.Api.Data;
using AppFileSync.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace AppFileSync.Api.Security;

public sealed class AppAuthorizationService(AppFileSyncDbContext db)
{
    public async Task<AppAuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, string appId, CancellationToken cancellationToken)
    {
        var ownerSubject = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(ownerSubject))
        {
            return AppAuthorizationResult.Unauthenticated();
        }

        var app = await db.Apps.AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.AppId == appId && candidate.IsEnabled, cancellationToken);

        if (app is null)
        {
            return AppAuthorizationResult.NotFound();
        }

        if (!TokenCanAccessApp(user, app))
        {
            return AppAuthorizationResult.Forbidden();
        }

        return AppAuthorizationResult.Allowed(ownerSubject, app);
    }

    private static bool TokenCanAccessApp(ClaimsPrincipal user, RegisteredApp app)
    {
        var allowedValues = user.FindAll("azp")
            .Concat(user.FindAll("client_id"))
            .Concat(user.FindAll("aud"))
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        return allowedValues.Contains(app.OidcClientId) || allowedValues.Contains(app.AppId);
    }
}

public sealed record AppAuthorizationResult(
    bool IsAllowed,
    string? OwnerSubject,
    RegisteredApp? App,
    ApiError? Error,
    int StatusCode)
{
    public static AppAuthorizationResult Allowed(string ownerSubject, RegisteredApp app)
    {
        return new AppAuthorizationResult(true, ownerSubject, app, null, StatusCodes.Status200OK);
    }

    public static AppAuthorizationResult Unauthenticated()
    {
        return new AppAuthorizationResult(false, null, null, new ApiError("unauthenticated"), StatusCodes.Status401Unauthorized);
    }

    public static AppAuthorizationResult Forbidden()
    {
        return new AppAuthorizationResult(false, null, null, new ApiError("forbidden_app"), StatusCodes.Status403Forbidden);
    }

    public static AppAuthorizationResult NotFound()
    {
        return new AppAuthorizationResult(false, null, null, new ApiError("not_found"), StatusCodes.Status404NotFound);
    }
}
