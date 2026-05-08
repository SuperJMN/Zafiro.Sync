using System.Net;
using System.Net.Http.Json;
using AppFileSync.Api.Data;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace AppFileSync.Api.Tests;

public sealed class FileEndpointsTests(AppFileSyncApiFactory factory) : IClassFixture<AppFileSyncApiFactory>
{
    [Fact]
    public async Task PutThenGet_ShouldRoundTripOpaquePayload()
    {
        var client = factory.CreateAuthenticatedClient("roundtrip-user");
        var request = new
        {
            baseRevision = (long?)null,
            deviceId = Guid.NewGuid(),
            encryptedMetadata = Convert.ToBase64String([1, 2, 3]),
            ciphertext = Convert.ToBase64String([4, 5, 6]),
            cipherHash = "sha256:test",
            plaintextSizeBytes = 3,
        };

        var put = await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-1", request);

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        var putBody = await put.Content.ReadFromJsonAsync<WriteResponse>();
        putBody.Should().BeEquivalentTo(new { Revision = 1L, Cursor = "1" });

        var get = await client.GetFromJsonAsync<FileResponse>("/v1/apps/fifo-calculator/files/file-1");

        get.Should().NotBeNull();
        get!.FileId.Should().Be("file-1");
        get.Revision.Should().Be(1);
        get.EncryptedMetadata.Should().Be(request.encryptedMetadata);
        get.Ciphertext.Should().Be(request.ciphertext);
    }

    [Fact]
    public async Task Put_WhenBaseRevisionIsStale_ShouldReturnConflict()
    {
        var client = factory.CreateAuthenticatedClient("conflict-user");
        await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-2", CreatePutRequest(null, "sha256:1"));
        await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-2", CreatePutRequest(1, "sha256:2"));

        var stale = await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-2", CreatePutRequest(1, "sha256:3"));

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await stale.Content.ReadFromJsonAsync<ApiError>();
        body.Should().BeEquivalentTo(new { Error = "conflict", CurrentRevision = 2L, CurrentCursor = "2" });
    }

    [Fact]
    public async Task Get_WhenFileBelongsToAnotherUser_ShouldReturnNotFound()
    {
        var owner = factory.CreateAuthenticatedClient("owner", "fifo-calculator-client");
        await owner.PutAsJsonAsync("/v1/apps/fifo-calculator/files/private-file", CreatePutRequest(null, "sha256:owner"));
        var other = factory.CreateAuthenticatedClient("other", "fifo-calculator-client");

        var response = await other.GetAsync("/v1/apps/fifo-calculator/files/private-file");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_WhenTokenIsForAnotherApp_ShouldReturnForbidden()
    {
        var client = factory.CreateAuthenticatedClient("forbidden-user", "other-client");

        var response = await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/file-3", CreatePutRequest(null, "sha256:test"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<ApiError>();
        body!.Error.Should().Be("forbidden_app");
    }

    [Fact]
    public async Task Changes_AfterDelete_ShouldReturnTombstone()
    {
        var client = factory.CreateAuthenticatedClient("delete-user");
        await client.PutAsJsonAsync("/v1/apps/fifo-calculator/files/deleted-file", CreatePutRequest(null, "sha256:create"));
        await client.DeleteAsJsonAsync("/v1/apps/fifo-calculator/files/deleted-file", new
        {
            baseRevision = 1,
            deviceId = Guid.NewGuid(),
        });

        var changes = await client.GetFromJsonAsync<ChangesResponse>("/v1/apps/fifo-calculator/changes?after=1");

        changes.Should().NotBeNull();
        changes!.NextCursor.Should().Be("2");
        changes.Changes.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new
            {
                FileId = "deleted-file",
                Revision = 2L,
                IsDeleted = true,
            }, options => options.ExcludingMissingMembers());
    }

    [Fact]
    public async Task Put_WhenPlaintextSizeExceedsAppLimit_ShouldReturnPayloadTooLarge()
    {
        var client = factory.CreateAuthenticatedClient("large-user", "tiny-client");

        var response = await client.PutAsJsonAsync("/v1/apps/tiny-app/files/file", CreatePutRequest(null, "sha256:large", plaintextSizeBytes: 11));

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadFromJsonAsync<ApiError>();
        body!.Error.Should().Be("file_too_large");
    }

    private static object CreatePutRequest(long? baseRevision, string cipherHash, int plaintextSizeBytes = 3)
    {
        return new
        {
            baseRevision,
            deviceId = Guid.NewGuid(),
            encryptedMetadata = Convert.ToBase64String([1, 2, 3]),
            ciphertext = Convert.ToBase64String([4, 5, 6]),
            cipherHash,
            plaintextSizeBytes,
        };
    }

    private sealed record WriteResponse(long Revision, string Cursor, DateTimeOffset UpdatedAt);
    private sealed record FileResponse(string FileId, long Revision, string EncryptedMetadata, string Ciphertext, string CipherHash, int PlaintextSizeBytes, int CiphertextSizeBytes, DateTimeOffset UpdatedAt);
    private sealed record ChangesResponse(string NextCursor, bool HasMore, IReadOnlyList<ChangeResponse> Changes);
    private sealed record ChangeResponse(string FileId, long Revision, bool IsDeleted, int PlaintextSizeBytes, int CiphertextSizeBytes, string CipherHash, string EncryptedMetadata);
    private sealed record ApiError(string Error, long? CurrentRevision = null, string? CurrentCursor = null);
}

internal static class HttpClientJsonExtensions
{
    public static HttpRequestMessage CreateJsonDeleteRequest(this HttpClient client, string requestUri, object value)
    {
        return new HttpRequestMessage(HttpMethod.Delete, requestUri)
        {
            Content = JsonContent.Create(value),
        };
    }

    public static async Task<HttpResponseMessage> DeleteAsJsonAsync(this HttpClient client, string requestUri, object value)
    {
        using var request = client.CreateJsonDeleteRequest(requestUri, value);
        return await client.SendAsync(request);
    }
}
