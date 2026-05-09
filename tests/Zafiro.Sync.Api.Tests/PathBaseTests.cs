using System.Net;
using FluentAssertions;

namespace Zafiro.Sync.Api.Tests;

public sealed class PathBaseTests(ZafiroSyncApiFactory factory) : IClassFixture<ZafiroSyncApiFactory>
{
    [Fact]
    public async Task Health_WhenPathBaseIsConfigured_ShouldRespondUnderPrefix()
    {
        var client = factory.CreateClientWithPathBase("/zafiro-sync");

        var response = await client.GetAsync("/zafiro-sync/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthorizedEndpoint_WhenPathBaseIsConfiguredAndTokenIsMissing_ShouldReturnUnauthorized()
    {
        var client = factory.CreateClientWithPathBase("/zafiro-sync");

        var response = await client.GetAsync("/zafiro-sync/v1/apps");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
