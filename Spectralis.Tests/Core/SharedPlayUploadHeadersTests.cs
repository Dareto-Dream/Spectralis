using System.Net;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SharedPlayUploadHeadersTests
{
    private const string Key = "abcdefgh12345678";

    /// <summary>A stand-in backend: answers the create call the way the real one does (the key in the response and again in
    /// the upload headers) and records what the package PUT carried.</summary>
    private sealed class FakeBackend : HttpMessageHandler
    {
        public HttpRequestMessage? Put { get; private set; }
        public string[] PutKeyValues { get; private set; } = [];
        public string[] PutContentTypes { get; private set; } = [];
        public bool PutAskedToContinue { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                var json = $$$"""
                    {"roomCode":"ABC123","sessionKey":"{{{Key}}}","trackId":"t1","expiresAtUtc":"2030-01-01T00:00:00Z",
                     "uploads":[{"name":"spectralis-package","method":"PUT","uploadUrl":"https://api.example.test/shared-play/v2/sessions/ABC123/package",
                       "headers":{"content-type":"application/vnd.spectralis.shared-play+zip","x-session-key":"{{{Key}}}"}}]}
                    """;
                return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(json) };
            }

            Put = request;
            PutKeyValues = request.Headers.TryGetValues("x-session-key", out var keys) ? keys.ToArray() : [];
            PutContentTypes = request.Content!.Headers.TryGetValues("Content-Type", out var types) ? types.ToArray() : [];
            PutAskedToContinue = request.Headers.ExpectContinue == true;
            await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private static SharedPlayPackage Package(string path) => new(
        "t1", path, "audio-sha", "package-sha", 10, new FileInfo(path).Length, ".mp3",
        new SharedPlayTrackDescriptor("Title", null, null, 1, "mp3", 2, 44100, 16, false, false, false, false, false, []),
        DateTimeOffset.UtcNow);

    [Fact]
    public async Task PackageUploadSendsTheSessionKeyOnceAndAsksTheServerFirst()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[4096]);
            var backend = new FakeBackend();
            var client = new SharedPlayCdnClient(new HttpClient(backend));

            await client.CreateSessionAndUploadAsync(
                new Uri("https://api.example.test"),
                Package(path),
                new SharedPlayPlaybackSnapshot(false, 0, 1, "test", DateTimeOffset.UtcNow),
                CancellationToken.None);

            // Sent twice, the backend reads it as "key, key", glues the halves together and refuses the host upload.
            Assert.Equal([Key], backend.PutKeyValues);
            Assert.Equal(["application/vnd.spectralis.shared-play+zip"], backend.PutContentTypes);
            // A refusal then comes back as its real status and message, not "Error while copying content to a stream".
            Assert.True(backend.PutAskedToContinue);
        }
        finally { File.Delete(path); }
    }
}
