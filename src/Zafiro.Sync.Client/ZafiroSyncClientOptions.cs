namespace Zafiro.Sync.Client;

public sealed class ZafiroSyncClientOptions
{
    public required Uri ServiceBaseUri { get; init; }

    public required string AppId { get; init; }

    public required Guid DeviceId { get; init; }

    public required byte[] AppDataKey { get; init; }
}
