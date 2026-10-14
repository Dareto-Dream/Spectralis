using System.Net;
using System.Text.Json;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public class SharedPlayListenerUploadTests
{
    private static SharedPlayRoomSession Session => new("ABC123", "ABC-123", "https://api.example.test", "t1",
        new Uri("https://api.example.test/shared-play/v2/sessions/ABC123/state"),
        new Uri("https://api.example.test/shared-play/v2/sessions/ABC123/queue"), null, "host-secret");
    private static SharedPlayQueueItem Item(string id) => new(id, "upload", "My song", null, null,
        $"https://api.example.test/shared-play/v2/sessions/ABC123/uploads/{id}/audio/My%20song.wav",
        id, null, "Alex", DateTimeOffset.UtcNow);

    private sealed class Backend : HttpMessageHandler
    {
        public int Downloads, Receipts;
        public bool FailReceipt = true;
        public bool Oversized;
        public List<string> Paths = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal("host-secret", request.Headers.GetValues("x-session-key").Single());
            Assert.Null(request.Headers.Authorization);
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get)
            {
                Downloads++;
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent("RIFFxxxxWAVEfmt data"u8.ToArray()) };
                if (Oversized) response.Content.Headers.ContentLength = 64L * 1024 * 1024 + 1;
                return Task.FromResult(response);
            }
            Receipts++;
            return Task.FromResult(new HttpResponseMessage(FailReceipt ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(FailReceipt ? "{\"error\":\"try again\"}" : "{\"ok\":true}") });
        }
    }

    [Fact]
    public async Task HostQueuesALocalFileBeforeAcknowledgingAndReceiptRetryDoesNotDuplicateIt()
    {
        var handler = new Backend();
        using var client = new SharedPlayCdnClient(new HttpClient(handler));
        var item = Item(Guid.NewGuid().ToString());
        string? local = null; var calls = 0;
        var receiver = new SharedPlayListenerUploads(client);
        Task Accept(string path) { Assert.True(File.Exists(path)); Assert.Equal("My song.wav", Path.GetFileName(path)); Assert.Equal(0, handler.Receipts); local = path; calls++; return Task.CompletedTask; }
        try
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => receiver.ReceiveAsync(Session,item,Accept,false,CancellationToken.None));
            Assert.Equal(1,calls);
            handler.FailReceipt = false;
            await receiver.ReceiveAsync(Session,item,Accept,false,CancellationToken.None);
            Assert.Equal(1,calls); Assert.Equal(1,handler.Downloads); Assert.Equal(2,handler.Receipts);
            Assert.EndsWith($"/uploads/{item.SourceId}/received", handler.Paths[^1]);
        }
        finally { receiver.Dispose(); if(local is not null) Directory.Delete(Path.GetDirectoryName(local)!,true); }
    }

    [Theory]
    [InlineData("https://other.test/shared-play/v2/sessions/ABC123/uploads/{0}/audio/song.wav")]
    [InlineData("https://api.example.test/shared-play/v2/sessions/XYZ123/uploads/{0}/audio/song.wav")]
    [InlineData("https://api.example.test/shared-play/v2/sessions/ABC123/uploads/{0}/audio/%2e%2e%2fsecret.wav")]
    [InlineData("https://api.example.test/shared-play/v2/sessions/ABC123/uploads/{0}/audio/song.wav?redirect=evil")]
    public async Task UploadCannotFetchAnotherOriginRoomOrFilesystemPath(string url)
    {
        var handler = new Backend(); using var client = new SharedPlayCdnClient(new HttpClient(handler));
        using var receiver = new SharedPlayListenerUploads(client);
        var item = Item(Guid.NewGuid().ToString()); item = item with { Url = string.Format(url,item.SourceId) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.ReceiveAsync(Session,item,_ => Task.CompletedTask,false,CancellationToken.None));
        Assert.Equal(0,handler.Downloads); Assert.Equal(0,handler.Receipts);
    }

    [Fact]
    public async Task OversizedUploadIsNeverQueuedOrAcknowledged()
    {
        var handler = new Backend { Oversized = true }; using var client = new SharedPlayCdnClient(new HttpClient(handler));
        var item = Item(Guid.NewGuid().ToString()); var receiver = new SharedPlayListenerUploads(client);
        var folder = Path.Combine(Path.GetTempPath(),"Spectralis","shared-play-uploads","ABC123-"+item.SourceId);
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => receiver.ReceiveAsync(Session,item,_ => throw new Exception("must not queue"),false,CancellationToken.None));
            Assert.Equal(0,handler.Receipts); Assert.Empty(Directory.GetFiles(folder,"*.wav"));
        }
        finally { receiver.Dispose(); Directory.Delete(folder,true); }
    }

    [Fact]
    public void AudioUploadsDefaultOnAndHostOptOutRoundTrips()
    {
        using var empty = JsonDocument.Parse("{}"); using var disabled = JsonDocument.Parse("{\"audioUploads\":false}");
        Assert.True(SharedPlayCapabilities.FromJson(empty.RootElement).AudioUploads);
        Assert.False(SharedPlayCapabilities.FromJson(disabled.RootElement).AudioUploads);
    }
}
