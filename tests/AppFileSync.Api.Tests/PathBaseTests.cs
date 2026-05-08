using System.Net;
using FluentAssertions;

namespace AppFileSync.Api.Tests;

public sealed class PathBaseTests(AppFileSyncApiFactory factory) : IClassFixture<AppFileSyncApiFactory>
{
    [Fact]
    public async Task Health_WhenPathBaseIsConfigured_ShouldRespondUnderPrefix()
    {
        var client = factory.CreateClientWithPathBase("/appfilesync");

        var response = await client.GetAsync("/appfilesync/healthz");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AuthorizedEndpoint_WhenPathBaseIsConfiguredAndTokenIsMissing_ShouldReturnUnauthorized()
    {
        var client = factory.CreateClientWithPathBase("/appfilesync");

        var response = await client.GetAsync("/appfilesync/v1/apps");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
