using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Zafiro.Sync.Api.Data;
using Zafiro.Sync.Api.Domain;
using Zafiro.Sync.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Zafiro.Sync.Api.Tests;

public sealed class AppIdentityAuthenticationTests(AppIdentityApiFactory factory)
    : IClassFixture<AppIdentityApiFactory>
{
    [Fact]
    public async Task CreateChallenge_ForRegisteredApp_ShouldReturnChallengeToSign()
    {
        var client = factory.CreateClient();
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");

        var response = await client.PostAsJsonAsync("/v1/auth/challenges", new
        {
            appId = identity.AppId,
            publicKey = identity.PublicKey,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ChallengeResponse>();
        body.Should().NotBeNull();
        body!.AppId.Should().Be(identity.AppId);
        body.PublicKey.Should().Be(identity.PublicKey);
        body.Challenge.Should().NotBeNullOrWhiteSpace();
        body.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateSession_WithValidSignature_ShouldReturnTokenThatCanSaveFiles()
    {
        var client = factory.CreateClient();
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");

        var token = await Authenticate(client, identity);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var put = await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-1", CreatePutRequest());

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ZafiroSyncDbContext>();
        var file = await db.Files.AsNoTracking().SingleAsync(file => file.FileId == "file-1");
        file.OwnerSubject.Should().Be(identity.Subject);
        file.AppId.Should().Be("fifo-calculator");
    }

    [Fact]
    public async Task CreateSession_WithInvalidSignature_ShouldReturnUnauthorized()
    {
        var client = factory.CreateClient();
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
        var challenge = await CreateChallenge(client, identity);

        var response = await client.PostAsJsonAsync("/v1/auth/sessions", new
        {
            challengeId = challenge.ChallengeId,
            publicKey = identity.PublicKey,
            signature = Encode([1, 2, 3, 4]),
            deviceId = identity.DeviceId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_ForOneIdentity_ShouldNotSeeAnotherIdentityFiles()
    {
        var ownerClient = factory.CreateClient();
        var ownerIdentity = AppIdentity.Create("fifo-calculator", "Owner");
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await Authenticate(ownerClient, ownerIdentity));
        await ownerClient.PutAsJsonAsync("/v1/apps/fifo-calculator/files/private-file", CreatePutRequest());

        var otherClient = factory.CreateClient();
        var otherIdentity = AppIdentity.Create("fifo-calculator", "Other");
        otherClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await Authenticate(otherClient, otherIdentity));

        var response = await otherClient.GetAsync("/v1/apps/fifo-calculator/files/private-file");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Token_ForOneApp_ShouldNotWriteAnotherApp()
    {
        var client = factory.CreateClient();
        var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await Authenticate(client, identity));

        var response = await client.PutAsJsonAsync("/v1/apps/tiny-app/files/file-1", CreatePutRequest());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateSession_WhenChallengeExpired_ShouldReturnUnauthorized()
    {
        var factory = new AppIdentityApiFactory(new Dictionary<string, string?>
        {
            ["Authentication:AppIdentity:ChallengeLifetimeSeconds"] = "1",
        });
        await factory.InitializeAsync();
        try
        {
            var client = factory.CreateClient();
            var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
            var challenge = await CreateChallenge(client, identity);

            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            var response = await client.PostAsJsonAsync("/v1/auth/sessions", new
            {
                challengeId = challenge.ChallengeId,
                publicKey = identity.PublicKey,
                signature = Encode(identity.Sign(Encoding.UTF8.GetBytes(challenge.Challenge))),
                deviceId = identity.DeviceId,
            });

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task Token_WhenExpired_ShouldReturnUnauthorized()
    {
        var factory = new AppIdentityApiFactory(new Dictionary<string, string?>
        {
            ["Authentication:AppIdentity:SessionLifetimeSeconds"] = "1",
        });
        await factory.InitializeAsync();
        try
        {
            var client = factory.CreateClient();
            var identity = AppIdentity.Create("fifo-calculator", "FIFO Calculator");
            var token = await Authenticate(client, identity);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            var response = await client.GetAsync("/v1/apps");

            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }
        finally
        {
            await factory.DisposeAsync();
        }
    }

    private static async Task<string> Authenticate(HttpClient client, AppIdentity identity)
    {
        var provider = new AppIdentityTokenProvider(client, client.BaseAddress!, identity);
        return await provider.GetAccessTokenAsync();
    }

    private static async Task<ChallengeResponse> CreateChallenge(HttpClient client, AppIdentity identity)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/challenges", new
        {
            appId = identity.AppId,
            publicKey = identity.PublicKey,
        });
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ChallengeResponse>()
            ?? throw new InvalidOperationException("Challenge response was empty.");
    }

    private static object CreatePutRequest()
    {
        return new
        {
            baseRevision = (long?)null,
            deviceId = Guid.NewGuid(),
            encryptedMetadata = Convert.ToBase64String([1, 2, 3]),
            ciphertext = Convert.ToBase64String([4, 5, 6]),
            cipherHash = "sha256:test",
            plaintextSizeBytes = 3,
        };
    }

    private static string Encode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private sealed record ChallengeResponse(Guid ChallengeId, string AppId, string PublicKey, string Challenge, DateTimeOffset ExpiresAt);
}

public sealed class AppIdentityApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly IReadOnlyDictionary<string, string?> configurationValues;

    public AppIdentityApiFactory()
        : this(new Dictionary<string, string?>())
    {
    }

    internal AppIdentityApiFactory(IReadOnlyDictionary<string, string?> configurationValues)
    {
        this.configurationValues = configurationValues;
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
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var values = new Dictionary<string, string?>
            {
                ["Authentication:Demo:Enabled"] = "false",
                ["Authentication:Authority"] = "",
                ["Authentication:Audience"] = "",
            };

            foreach (var pair in configurationValues)
            {
                values[pair.Key] = pair.Value;
            }

            configuration.AddInMemoryCollection(values);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ZafiroSyncDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<ZafiroSyncDbContext>();
            services.AddDbContext<ZafiroSyncDbContext>(options => options.UseSqlite(connection));
        });
    }
}
