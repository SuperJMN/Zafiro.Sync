using System.Security.Cryptography;
using System.Text;
using Zafiro.Sync.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IConfiguration>()
        .GetSection("DemoClient")
        .Get<DemoClientOptions>() ?? new DemoClientOptions();

    return options.Validate();
});

builder.Services.AddHttpClient();
builder.Services.AddSingleton<DemoIdentityStore>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/settings", async (
    DemoClientOptions options,
    DemoIdentityStore identityStore,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var identity = await identityStore.Load(cancellationToken);
    if (identity is null)
    {
        return Results.Conflict(new { error = "identity_missing" });
    }

    var client = CreateSyncClient(options, identity, httpClientFactory);
    var result = await client.LoadAsync(options.LogicalPath, cancellationToken);

    return result switch
    {
        LoadFileResult.Found found => Results.Ok(DemoSettingsResponse.Found(
            found.Revision,
            found.FileId,
            Encoding.UTF8.GetString(found.Content))),
        LoadFileResult.NotFound => Results.Ok(DemoSettingsResponse.Empty()),
        _ => Results.Problem("Unexpected load result."),
    };
});

app.MapPost("/api/settings", async (
    SaveSettingsRequest request,
    DemoClientOptions options,
    DemoIdentityStore identityStore,
    IHttpClientFactory httpClientFactory,
    CancellationToken cancellationToken) =>
{
    var identity = await identityStore.Load(cancellationToken);
    if (identity is null)
    {
        return Results.Conflict(new SaveSettingsResponse(
            "identity_missing",
            0,
            "",
            "0",
            "Create or import an identity before saving."));
    }

    var client = CreateSyncClient(options, identity, httpClientFactory);
    var result = await client.SaveAsync(
        options.LogicalPath,
        Encoding.UTF8.GetBytes(request.Content),
        "application/json",
        new Dictionary<string, string> { ["kind"] = "demo-settings" },
        request.BaseRevision,
        cancellationToken);

    return result switch
    {
        SaveFileResult.Saved saved => Results.Ok(new SaveSettingsResponse(
            "saved",
            saved.Revision,
            saved.FileId,
            saved.Cursor,
            null)),
        SaveFileResult.Conflict conflict => Results.Conflict(new SaveSettingsResponse(
            "conflict",
            conflict.CurrentRevision,
            conflict.FileId,
            conflict.CurrentCursor,
            "Remote content changed before this save.")),
        _ => Results.Problem("Unexpected save result."),
    };
});

app.MapGet("/api/identity", async (
    DemoClientOptions options,
    DemoIdentityStore identityStore,
    CancellationToken cancellationToken) =>
{
    var identity = await identityStore.Load(cancellationToken);

    return Results.Ok(identity is null
        ? DemoIdentityResponse.Empty(options.AppId)
        : DemoIdentityResponse.From(identity));
});

app.MapPost("/api/identity", async (
    DemoIdentityStore identityStore,
    CancellationToken cancellationToken) =>
{
    var identity = await identityStore.Load(cancellationToken) ??
                   await identityStore.Create(cancellationToken);

    return Results.Ok(DemoIdentityResponse.From(identity));
});

app.MapPost("/api/identity/export", async (
    IdentityExportRequest request,
    DemoIdentityStore identityStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Password))
    {
        return Results.BadRequest(new { error = "password_required" });
    }

    var identity = await identityStore.Load(cancellationToken);
    if (identity is null)
    {
        return Results.Conflict(new { error = "identity_missing" });
    }

    var bytes = identity.Export(request.Password);
    return Results.File(bytes, "application/json", $"zafiro-sync-{identity.AppId}-identity.json");
});

app.MapPost("/api/identity/import", async (
    IdentityImportRequest request,
    DemoIdentityStore identityStore,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Password) ||
        string.IsNullOrWhiteSpace(request.ExportJson))
    {
        return Results.BadRequest(new { error = "invalid_request" });
    }

    AppIdentity identity;
    try
    {
        identity = AppIdentity.Import(request.Password, Encoding.UTF8.GetBytes(request.ExportJson));
        await identityStore.Save(identity, cancellationToken);
    }
    catch (Exception ex) when (ex is CryptographicException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = "invalid_identity" });
    }

    return Results.Ok(DemoIdentityResponse.From(identity));
});

app.Run();

static ZafiroSyncClient CreateSyncClient(DemoClientOptions options, AppIdentity identity, IHttpClientFactory httpClientFactory)
{
    var httpClient = httpClientFactory.CreateClient(nameof(ZafiroSyncClient));
    httpClient.BaseAddress = options.ServiceBaseUri;
    var encryptor = new AesGcmFileEncryptor(identity.AppDataKey);

    return new ZafiroSyncClient(
        httpClient,
        new ZafiroSyncClientOptions
        {
            ServiceBaseUri = options.ServiceBaseUri,
            AppId = identity.AppId,
            DeviceId = identity.DeviceId,
            AppDataKey = identity.AppDataKey,
        },
        new AppIdentityTokenProvider(httpClient, options.ServiceBaseUri, identity),
        encryptor);
}

public sealed class DemoClientOptions
{
    public Uri ServiceBaseUri { get; init; } = new("https://filesync.superjmn.com");

    public string AppId { get; init; } = "fifo-calculator";

    public string LogicalPath { get; init; } = "settings/demo.json";

    public string IdentityFilePath { get; init; } = "settings/demo.identity.json";

    public string LocalIdentityPassword { get; init; } = "zafiro-sync-demo-local";

    public DemoClientOptions Validate()
    {
        if (ServiceBaseUri is null)
        {
            throw new InvalidOperationException("DemoClient:ServiceBaseUri is required.");
        }

        if (string.IsNullOrWhiteSpace(AppId))
        {
            throw new InvalidOperationException("DemoClient:AppId is required.");
        }

        if (string.IsNullOrWhiteSpace(IdentityFilePath))
        {
            throw new InvalidOperationException("DemoClient:IdentityFilePath is required.");
        }

        if (string.IsNullOrWhiteSpace(LocalIdentityPassword))
        {
            throw new InvalidOperationException("DemoClient:LocalIdentityPassword is required.");
        }

        _ = Zafiro.Sync.Client.LogicalPath.Normalize(LogicalPath);

        return this;
    }
}

public sealed class DemoIdentityStore(DemoClientOptions options)
{
    public async Task<AppIdentity?> Load(CancellationToken cancellationToken)
    {
        var path = Path.GetFullPath(options.IdentityFilePath);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
        return AppIdentity.Import(options.LocalIdentityPassword, bytes);
    }

    public async Task<AppIdentity> Create(CancellationToken cancellationToken)
    {
        var identity = AppIdentity.Create(options.AppId, "Zafiro.Sync Demo");
        await Save(identity, cancellationToken);

        return identity;
    }

    public async Task Save(AppIdentity identity, CancellationToken cancellationToken)
    {
        if (identity.AppId != options.AppId)
        {
            throw new InvalidOperationException($"The imported identity belongs to '{identity.AppId}', not '{options.AppId}'.");
        }

        var path = Path.GetFullPath(options.IdentityFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        await File.WriteAllBytesAsync(path, identity.Export(options.LocalIdentityPassword), cancellationToken);
    }
}

public sealed record SaveSettingsRequest(string Content, long? BaseRevision);

public sealed record IdentityExportRequest(string Password);

public sealed record IdentityImportRequest(string Password, string ExportJson);

public sealed record DemoIdentityResponse(
    bool Exists,
    string AppId,
    Guid? DeviceId,
    string? Subject,
    string? PublicKey)
{
    public static DemoIdentityResponse Empty(string appId)
    {
        return new DemoIdentityResponse(false, appId, null, null, null);
    }

    public static DemoIdentityResponse From(AppIdentity identity)
    {
        return new DemoIdentityResponse(true, identity.AppId, identity.DeviceId, identity.Subject, identity.PublicKey);
    }
}

public sealed record DemoSettingsResponse(
    bool Exists,
    long? Revision,
    string? FileId,
    string Content)
{
    public static DemoSettingsResponse Empty()
    {
        return new DemoSettingsResponse(false, null, null, DefaultContent);
    }

    public static DemoSettingsResponse Found(long revision, string fileId, string content)
    {
        return new DemoSettingsResponse(true, revision, fileId, content);
    }

    private const string DefaultContent = """
{
  "theme": "dark",
  "currency": "EUR",
  "autosave": true
}
""";
}

public sealed record SaveSettingsResponse(
    string Status,
    long Revision,
    string FileId,
    string Cursor,
    string? Message);
