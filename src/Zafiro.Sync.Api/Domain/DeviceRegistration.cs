namespace Zafiro.Sync.Api.Domain;

public sealed class DeviceRegistration
{
    public Guid Id { get; set; }

    public string OwnerSubject { get; set; } = "";

    public string AppId { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public byte[] PublicKey { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAt { get; set; }
}
