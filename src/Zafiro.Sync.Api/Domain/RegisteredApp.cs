namespace Zafiro.Sync.Api.Domain;

public sealed class RegisteredApp
{
    public string AppId { get; set; } = "";

    public string OidcClientId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public int MaxPlaintextBytes { get; set; } = 5 * 1024 * 1024;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
