namespace AppFileSync.Client;

public interface IAppFileSyncTokenProvider
{
    ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}

public sealed class StaticTokenProvider(string token) : IAppFileSyncTokenProvider
{
    public ValueTask<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(token);
    }
}
