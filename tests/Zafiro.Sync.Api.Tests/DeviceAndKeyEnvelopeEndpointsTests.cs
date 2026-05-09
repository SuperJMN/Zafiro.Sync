using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Zafiro.Sync.Api.Tests;

public sealed class DeviceAndKeyEnvelopeEndpointsTests(ZafiroSyncApiFactory factory) : IClassFixture<ZafiroSyncApiFactory>
{
    [Fact]
    public async Task RegisterDeviceThenList_ShouldReturnDeviceForCurrentUserAndApp()
    {
        var client = factory.CreateAuthenticatedClient("device-user");
        var deviceId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/v1/apps/fifo-calculator/devices", new
        {
            deviceId,
            displayName = "Framework Laptop",
            publicKey = Convert.ToBase64String([1, 2, 3, 4]),
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var devices = await client.GetFromJsonAsync<DevicesResponse>("/v1/apps/fifo-calculator/devices");

        devices.Should().NotBeNull();
        devices!.Devices.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                DeviceId = deviceId,
                DisplayName = "Framework Laptop",
                PublicKey = Convert.ToBase64String([1, 2, 3, 4]),
            }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task AddKeyEnvelopeThenList_ShouldReturnEnvelopeForCurrentUserAndApp()
    {
        var client = factory.CreateAuthenticatedClient("envelope-user");
        var deviceId = Guid.NewGuid();
        var encryptedAppKey = Convert.ToBase64String([8, 9, 10]);

        var response = await client.PostAsJsonAsync("/v1/apps/fifo-calculator/key-envelopes", new
        {
            deviceId,
            envelopeVersion = 1,
            encryptedAppKey,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var envelopes = await client.GetFromJsonAsync<KeyEnvelopesResponse>("/v1/apps/fifo-calculator/key-envelopes");

        envelopes.Should().NotBeNull();
        envelopes!.KeyEnvelopes.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                DeviceId = deviceId,
                EnvelopeVersion = 1,
                EncryptedAppKey = encryptedAppKey,
            }, options => options.ExcludingMissingMembers());
    }

    private sealed record DevicesResponse(IReadOnlyList<DeviceResponse> Devices);
    private sealed record DeviceResponse(Guid DeviceId, string DisplayName, string PublicKey, DateTimeOffset CreatedAt, DateTimeOffset? RevokedAt);
    private sealed record KeyEnvelopesResponse(IReadOnlyList<KeyEnvelopeResponse> KeyEnvelopes);
    private sealed record KeyEnvelopeResponse(Guid DeviceId, int EnvelopeVersion, string EncryptedAppKey, DateTimeOffset CreatedAt);
}
