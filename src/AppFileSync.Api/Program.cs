using AppFileSync.Api.Configuration;
using AppFileSync.Api.Data;
using AppFileSync.Api.Endpoints;
using AppFileSync.Api.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.Configure<AppFileSyncOptions>(builder.Configuration.GetSection("AppFileSync"));
builder.Services.AddMemoryCache();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddDbContext<AppFileSyncDbContext>(options =>
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? "Host=localhost;Port=5432;Database=appfilesync;Username=appfilesync;Password=appfilesync";
        options.UseNpgsql(connectionString);
    });
}
builder.Services.AddHostedService<AppRegistrationHostedService>();
builder.Services.AddScoped<AppAuthorizationService>();
builder.Services.AddAppFileSyncAuthentication(builder.Configuration);
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

app.MapAppFileSyncEndpoints();

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
