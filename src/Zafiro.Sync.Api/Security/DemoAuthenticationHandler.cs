using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Zafiro.Sync.Api.Security;

public static class DemoAuthenticationDefaults
{
    public const string Scheme = "DemoBearer";
    public const string Subject = "demo-user";
    public const string AuthorizedParty = "fifo-calculator-client";
}

public sealed class DemoAuthenticationOptions : AuthenticationSchemeOptions
{
    public string AccessToken { get; set; } = "";

    public string Subject { get; set; } = DemoAuthenticationDefaults.Subject;

    public string AuthorizedParty { get; set; } = DemoAuthenticationDefaults.AuthorizedParty;
}

public sealed class DemoAuthenticationHandler(
    IOptionsMonitor<DemoAuthenticationOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<DemoAuthenticationOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = ReadBearerToken();
        if (token is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!TokenMatches(token, Options.AccessToken))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid demo bearer token."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, Options.Subject),
            new Claim("sub", Options.Subject),
            new Claim("azp", Options.AuthorizedParty),
            new Claim("client_id", Options.AuthorizedParty),
            new Claim("aud", Options.AuthorizedParty),
        };
        var identity = new ClaimsIdentity(claims, DemoAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, DemoAuthenticationDefaults.Scheme);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private string? ReadBearerToken()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return header["Bearer ".Length..].Trim();
    }

    private static bool TokenMatches(string candidate, string expected)
    {
        var candidateBytes = Encoding.UTF8.GetBytes(candidate);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return candidateBytes.Length == expectedBytes.Length &&
               CryptographicOperations.FixedTimeEquals(candidateBytes, expectedBytes);
    }
}
