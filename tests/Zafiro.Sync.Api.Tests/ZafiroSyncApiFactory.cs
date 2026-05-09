using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Zafiro.Sync.Api.Tests;

public sealed class ZafiroSyncApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");

    public ZafiroSyncDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ZafiroSyncDbContext>();
    }

    public HttpClient CreateAuthenticatedClient(string subject = "user-1", string authorizedParty = "fifo-calculator-client")
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add(TestAuthHandler.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(TestAuthHandler.AuthorizedPartyHeader, authorizedParty);
        return client;
    }

    public HttpClient CreateClientWithPathBase(string pathBase)
    {
        return WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Http:PathBase"] = pathBase,
                });
            });
        }).CreateClient();
    }

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZafiroSyncDbContext>();
        await db.Database.EnsureCreatedAsync();
        db.Apps.Add(new RegisteredApp
        {
            AppId = "fifo-calculator",
            OidcClientId = "fifo-calculator-client",
            DisplayName = "FIFO Calculator",
            MaxPlaintextBytes = 1024,
            IsEnabled = true,
        });
        db.Apps.Add(new RegisteredApp
        {
            AppId = "tiny-app",
            OidcClientId = "tiny-client",
            DisplayName = "Tiny App",
            MaxPlaintextBytes = 10,
            IsEnabled = true,
        });
        await db.SaveChangesAsync();
    }

    public new async Task DisposeAsync()
    {
        await connection.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ZafiroSyncDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<ZafiroSyncDbContext>();
            services.AddDbContext<ZafiroSyncDbContext>(options => options.UseSqlite(connection));

            services.AddAuthentication(TestAuthHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            });
        });
    }
}

public sealed class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Test";
    public const string SubjectHeader = "X-Test-Subject";
    public const string AuthorizedPartyHeader = "X-Test-Azp";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(SubjectHeader, out var subject) ||
            !Request.Headers.TryGetValue(AuthorizedPartyHeader, out var authorizedParty))
        {
            return Task.FromResult(AuthenticateResult.Fail("Missing test auth headers."));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, subject.ToString()),
            new Claim("sub", subject.ToString()),
            new Claim("azp", authorizedParty.ToString()),
            new Claim("client_id", authorizedParty.ToString()),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
