using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AppFileSync.Client.Tests;

public sealed class AppFileSyncClientSyncTests
{
    [Fact]
    public async Task SyncNow_WhenCalledAgain_ShouldContinueFromSavedCursor()
    {
        var requests = new List<Uri>();
        var handler = new StubHandler(request =>
        {
            requests.Add(request.RequestUri!);
            var nextCursor = requests.Count == 1 ? "5" : "9";
            return new
            {
                nextCursor,
                hasMore = false,
                changes = new[]
                {
                    new
                    {
                        fileId = $"file-{nextCursor}",
                        revision = 1,
                        isDeleted = false,
                        plaintextSizeBytes = 2,
                        ciphertextSizeBytes = 3,
                        cipherHash = "sha256:test",
                        encryptedMetadata = Convert.ToBase64String([1, 2]),
                    },
                },
            };
        });
        var client = new AppFileSyncClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://sync.test") },
            new AppFileSyncClientOptions
            {
                ServiceBaseUri = new Uri("https://sync.test"),
                AppId = "fifo-calculator",
                DeviceId = Guid.NewGuid(),
                AppDataKey = new byte[32],
            },
            new StaticTokenProvider("token"),
            new FakeFileEncryptor(),
            new InMemoryAppFileSyncStateStore());

        var first = await client.SyncNowAsync();
        var second = await client.SyncNowAsync();

        first.NextCursor.Should().Be("5");
        second.NextCursor.Should().Be("9");
        requests.Select(uri => uri.Query).Should().Equal("?after=0", "?after=5");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, object> createBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(createBody(request), options: new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            };

            return Task.FromResult(response);
        }
    }
}
