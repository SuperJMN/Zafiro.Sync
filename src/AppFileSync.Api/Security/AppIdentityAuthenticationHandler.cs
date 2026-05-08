using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AppFileSync.Api.Security;

public sealed class AppIdentityAuthenticationHandler(
    IOptionsMonitor<AppIdentityAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    AppIdentityTokenService tokenService)
    : AuthenticationHandler<AppIdentityAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!tokenService.TryValidate(token, out var payload))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid AppIdentity bearer token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, payload.Subject),
            new Claim("sub", payload.Subject),
            new Claim("azp", payload.AppId),
            new Claim("client_id", payload.AppId),
            new Claim("aud", payload.AppId),
            new Claim("device_id", payload.DeviceId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, AppIdentityAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AppIdentityAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : null;
    }
}
