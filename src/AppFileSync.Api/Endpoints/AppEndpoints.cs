using System.Security.Claims;
using AppFileSync.Api.Data;
using AppFileSync.Api.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppFileSync.Api.Endpoints;

public static partial class AppFileSyncEndpoints
{
    private static async Task<IResult> ListApps(
        HttpContext httpContext,
        [FromServices] AppFileSyncDbContext db,
        CancellationToken cancellationToken)
    {
        var apps = await db.Apps.AsNoTracking()
            .Where(app => app.IsEnabled)
            .OrderBy(app => app.AppId)
            .ToArrayAsync(cancellationToken);

        var visible = apps.Where(app => TokenCanAccessApp(httpContext.User, app))
            .Select(app => new AppResponse(app.AppId, app.DisplayName, app.MaxPlaintextBytes))
            .ToArray();

        return Results.Ok(new AppsResponse(visible));
    }

    private static bool TokenCanAccessApp(ClaimsPrincipal user, RegisteredApp app)
    {
        var values = user.FindAll("azp")
            .Concat(user.FindAll("client_id"))
            .Concat(user.FindAll("aud"))
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        return values.Contains(app.OidcClientId) || values.Contains(app.AppId);
    }
}
