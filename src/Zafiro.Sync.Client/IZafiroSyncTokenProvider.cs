namespace Zafiro.Sync.Client;

public interface IZafiroSyncTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class StaticTokenProvider(string token) : IZafiroSyncTokenProvider
{
    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(token);
    }
}
