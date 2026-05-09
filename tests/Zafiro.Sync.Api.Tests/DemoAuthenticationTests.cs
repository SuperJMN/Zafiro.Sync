using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Zafiro.Sync.Api.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zafiro.Sync.Api.Tests;

public sealed class DemoAuthenticationTests(DemoAuthenticationApiFactory factory)
    : IClassFixture<DemoAuthenticationApiFactory>
{
    [Fact]
    public async Task ListApps_WithDemoToken_ShouldReturnAuthorizedApp()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", DemoAuthenticationApiFactory.Token);

        var response = await client.GetFromJsonAsync<AppsResponse>("/v1/apps");

        response.Should().NotBeNull();
        response!.Apps.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                AppId = "fifo-calculator",
                DisplayName = "FIFO Calculator",
            }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task ListApps_WithWrongDemoToken_ShouldReturnUnauthorized()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await client.GetAsync("/v1/apps");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed record AppsResponse(IReadOnlyList<AppResponse> Apps);
    private sealed record AppResponse(string AppId, string DisplayName, int MaxPlaintextBytes);
}

public sealed class DemoAuthenticationApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Token = "demo-token-for-tests";
    private readonly SqliteConnection connection = new("Data Source=:memory:");

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
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Demo:Enabled"] = "true",
                ["Authentication:Demo:AccessToken"] = Token,
                ["Authentication:Demo:Subject"] = "demo-user",
                ["Authentication:Demo:AuthorizedParty"] = "fifo-calculator-client",
            });
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ZafiroSyncDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<ZafiroSyncDbContext>();
            services.AddDbContext<ZafiroSyncDbContext>(options => options.UseSqlite(connection));

            services.AddAuthentication(DemoAuthenticationDefaults.Scheme)
                .AddScheme<DemoAuthenticationOptions, DemoAuthenticationHandler>(
                    DemoAuthenticationDefaults.Scheme,
                    options =>
                    {
                        options.AccessToken = Token;
                        options.Subject = "demo-user";
                        options.AuthorizedParty = "fifo-calculator-client";
                    });

            services.PostConfigure<AuthenticationOptions>(options =>
            {
                options.DefaultAuthenticateScheme = DemoAuthenticationDefaults.Scheme;
                options.DefaultChallengeScheme = DemoAuthenticationDefaults.Scheme;
            });
        });
    }

}
