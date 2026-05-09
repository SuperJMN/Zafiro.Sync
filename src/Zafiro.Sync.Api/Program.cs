using Zafiro.Sync.Api.Configuration;
using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Endpoints;
using Zafiro.Sync.Api.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<ZafiroSyncOptions>(builder.Configuration.GetSection("ZafiroSync"));
builder.Services.AddMemoryCache();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<ZafiroSyncDbContext>(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=zafiro-sync;Username=zafiro-sync;Password=zafiro-sync";
        options.UseNpgsql(connectionString);
    });
}
builder.Services.AddHostedService<AppRegistrationHostedService>();
builder.Services.AddScoped<AppAuthorizationService>();
builder.Services.AddZafiroSyncAuthentication(builder.Configuration);
builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var pathBase = app.Configuration["Http:PathBase"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(NormalizePathBase(pathBase));
}

app.UseAuthentication();
app.UseAuthorization();

app.MapZafiroSyncEndpoints();

app.Run();

static PathString NormalizePathBase(string pathBase)
{
    var normalized = pathBase.Trim();
    if (!normalized.StartsWith('/'))
    {
        normalized = $"/{normalized}";
    }

    return new PathString(normalized.TrimEnd('/'));
}

public partial class Program;
