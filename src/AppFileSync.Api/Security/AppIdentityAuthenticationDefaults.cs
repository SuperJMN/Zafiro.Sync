namespace AppFileSync.Api.Security;

public static class AppIdentityAuthenticationDefaults
{
    public const string Scheme = "AppIdentityBearer";
    public const string PolicyScheme = "AppFileSyncAuth";
    public const string TokenPrefix = "afs1.";
}
