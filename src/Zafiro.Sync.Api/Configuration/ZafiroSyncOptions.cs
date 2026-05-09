namespace Zafiro.Sync.Api.Configuration;

public sealed class ZafiroSyncOptions
{
    public bool MigrateOnStartup { get; set; }

    public List<AppRegistrationOptions> Apps { get; set; } = [];
}

public sealed class AppRegistrationOptions
{
    public string AppId { get; set; } = "";

    public string OidcClientId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int MaxPlaintextBytes { get; set; } = 5 * 1024 * 1024;

    public bool IsEnabled { get; set; } = true;
}
