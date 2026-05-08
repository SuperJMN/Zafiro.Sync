namespace AppFileSync.Api;

public sealed record ApiError(
    string Error,
    long? CurrentRevision = null,
    string? CurrentCursor = null);
