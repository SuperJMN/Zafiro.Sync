namespace Zafiro.Sync.Client;

public static class LogicalPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var cleaned = path.Trim().Replace('\\', '/');
        var segments = cleaned
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => segment.Trim())
            .ToArray();

        if (segments.Length == 0)
        {
            throw new ArgumentException("The logical path must contain at least one segment.", nameof(path));
        }

        if (segments.Any(segment => segment is "." or ".." || segment.Contains(':')))
        {
            throw new ArgumentException("The logical path must stay inside the app namespace.", nameof(path));
        }

        return string.Join('/', segments);
    }
}
