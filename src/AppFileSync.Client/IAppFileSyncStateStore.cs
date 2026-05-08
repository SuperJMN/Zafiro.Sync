namespace AppFileSync.Client;

public interface IAppFileSyncStateStore
{
    ValueTask<string?> LoadCursorAsync(string appId, CancellationToken cancellationToken = default);

    ValueTask SaveCursorAsync(string appId, string cursor, CancellationToken cancellationToken = default);
}

public sealed class InMemoryAppFileSyncStateStore : IAppFileSyncStateStore
{
    private readonly Dictionary<string, string> cursors = new(StringComparer.Ordinal);

    public ValueTask<string?> LoadCursorAsync(string appId, CancellationToken cancellationToken = default)
    {
        cursors.TryGetValue(appId, out var cursor);
        return ValueTask.FromResult(cursor);
    }

    public ValueTask SaveCursorAsync(string appId, string cursor, CancellationToken cancellationToken = default)
    {
        cursors[appId] = cursor;
        return ValueTask.CompletedTask;
    }
}
