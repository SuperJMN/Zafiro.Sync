namespace AppFileSync.Api.Security;

public sealed class AppIdentityAuthenticationOptions : Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions
{
    public int ChallengeLifetimeSeconds { get; set; } = 300;

    public int SessionLifetimeSeconds { get; set; } = 900;
}
