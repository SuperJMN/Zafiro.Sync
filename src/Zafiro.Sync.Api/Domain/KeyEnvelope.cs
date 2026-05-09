namespace Zafiro.Sync.Api.Domain;

public sealed class KeyEnvelope
{
    public Guid Id { get; set; }

    public string OwnerSubject { get; set; } = "";

    public string AppId { get; set; } = "";

    public Guid DeviceId { get; set; }

    public int EnvelopeVersion { get; set; }

    public byte[] EncryptedAppKey { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
