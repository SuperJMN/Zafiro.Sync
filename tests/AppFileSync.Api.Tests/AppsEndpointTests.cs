using System.Net.Http.Json;
using FluentAssertions;

namespace AppFileSync.Api.Tests;

public sealed class AppsEndpointTests(AppFileSyncApiFactory factory) : IClassFixture<AppFileSyncApiFactory>
{
    [Fact]
    public async Task ListApps_ShouldOnlyReturnAppsAllowedForToken()
    {
        var client = factory.CreateAuthenticatedClient("apps-user", "fifo-calculator-client");

        var response = await client.GetFromJsonAsync<AppsResponse>("/v1/apps");

        response.Should().NotBeNull();
        response!.Apps.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                AppId = "fifo-calculator",
                DisplayName = "FIFO Calculator",
                MaxPlaintextBytes = 1024,
            });
    }

    private sealed record AppsResponse(IReadOnlyList<AppResponse> Apps);
    private sealed record AppResponse(string AppId, string DisplayName, int MaxPlaintextBytes);
}
