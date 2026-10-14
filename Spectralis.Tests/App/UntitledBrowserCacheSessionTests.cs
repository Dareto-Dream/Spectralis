using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Spectralis.App.Services;
using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.App;

public class UntitledBrowserCacheSessionTests
{
    private static readonly Uri Source = new("https://untitled.stream/library/track/test-track");

    [Fact]
    public async Task BrowserTransfer_CachesBytesAndPreservesTrackMetadata()
    {
        var host = new Host();
        using var session = new UntitledBrowserCacheSession(host, Source);
        var ready = new TaskCompletionSource<RemoteAudioResolveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Completed += (_, result) => ready.SetResult(result);
        await session.StartAsync();
        var bytes = Enumerable.Range(0, 45003).Select(i => (byte)i).ToArray();
        await Begin(host, bytes.Length);
        host.Send("chunk", 1, new { data = Convert.ToBase64String(bytes[..24000]) });
        await host.Ack(1);
        host.Send("chunk", 2, new { data = Convert.ToBase64String(bytes[24000..]) });
        await host.Ack(2);
        host.Send("end", 3, new { bytes = bytes.Length });
        var result = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.False(result.IsWebViewFallback());
            Assert.Equal("captured track", result.Title);
            Assert.Equal("captured artist", result.Artist);
            Assert.Equal(93.14, result.Duration.TotalSeconds);
            Assert.Equal(Source.AbsoluteUri, result.SourceUrl);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(result.CachedAudioPath!));
        }
        finally { File.Delete(result.CachedAudioPath!); }
    }

    [Theory]
    [InlineData("order")]
    [InlineData("length")]
    [InlineData("base64")]
    public async Task BrowserTransfer_RejectsBrokenTransfersAndDeletesPartialCache(string failure)
    {
        var host = new Host();
        using var session = new UntitledBrowserCacheSession(host, Source);
        var completed = false;
        var error = false;
        session.Completed += (_, _) => completed = true;
        session.Failed += (_, _) => error = true;
        await session.StartAsync();
        await Begin(host, 500);
        var partial = host.CachePath!;
        if (failure == "order") host.Send("chunk", 2, new { data = "AQID" });
        else if (failure == "base64") host.Send("chunk", 1, new { data = "invalid base64!" });
        else
        {
            host.Send("chunk", 1, new { data = "AQID" });
            await host.Ack(1);
            host.Send("end", 2, new { bytes = 3 });
        }
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(error);
        Assert.False(completed);
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task BrowserTransfer_CancellationDeletesPartialFile()
    {
        var host = new Host();
        var session = new UntitledBrowserCacheSession(host, Source);
        await session.StartAsync();
        await Begin(host, 3);
        host.Send("chunk", 1, new { data = "AQID" });
        await host.Ack(1);
        var partial = host.CachePath!;
        session.Dispose();
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(File.Exists(partial));
    }

    [Fact]
    public async Task BrowserTransfer_RejectsDifferentTrackAndIgnoresDifferentSession()
    {
        var host = new Host();
        using var session = new UntitledBrowserCacheSession(host, Source);
        var failed = false;
        session.Failed += (_, _) => failed = true;
        await session.StartAsync();
        host.Emit(JsonSerializer.Serialize(new { type = "untitled-cache", session = "wrong", kind = "error" }));
        Assert.False(session.Completion.IsCompleted);
        host.Send("begin", 0, new { sourceUrl = "https://untitled.stream/library/track/other", duration = 0 });
        await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(failed);
    }

    private static async Task Begin(Host host, int bytes)
    {
        host.Send("begin", 0, new
        {
            sourceUrl = Source.AbsoluteUri, title = "captured track", artist = "captured artist",
            duration = 93.14, expectedBytes = bytes
        });
        await host.Ack(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrowserTransfer_RecoversAfterChallengeOrDocumentReplacement(bool interrupted)
    {
        var host = new Host();
        using var session = new UntitledBrowserCacheSession(host, Source);
        var ready = new TaskCompletionSource<RemoteAudioResolveResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.Completed += (_, result) => ready.SetResult(result);
        session.Failed += (_, error) => failed.TrySetResult(error);
        await session.StartAsync();
        string? oldPath = null;
        if (interrupted)
        {
            await Begin(host, 500);
            host.Send("chunk", 1, new { data = "AQID" });
            await host.Ack(1);
            oldPath = host.CachePath;
            host.Send("reset", 0, new { });
        }
        else
        {
            host.Send("error", 0, new { retryable = true, stage = "signing", httpStatus = 403 });
            Assert.Contains("signing (HTTP 403)", await failed.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        Assert.False(session.Completion.IsCompleted);
        await Begin(host, 3);
        host.Send("chunk", 1, new { data = "BAUG" });
        await host.Ack(1);
        host.Send("end", 2, new { bytes = 3 });
        var result = await ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            Assert.Equal(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(result.CachedAudioPath!));
            if (oldPath is not null) Assert.False(File.Exists(oldPath));
        }
        finally { File.Delete(result.CachedAudioPath!); }
    }

    private sealed class Host : IWebViewHost
    {
        private readonly Channel<int> _acks = Channel.CreateUnbounded<int>();
        private HashSet<string> _before = [];
        public string Session { get; private set; } = "";
        public string? CachePath { get; private set; }
        public event EventHandler<string>? MessageReceived;
        public event EventHandler? NavigationCompleted { add { } remove { } }
        public event EventHandler? NavigationFailed { add { } remove { } }
        public void Emit(string message) => MessageReceived?.Invoke(this, message);
        public void Send(string kind, int sequence, object fields)
        {
            var value = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(fields))!;
            value["type"] = JsonSerializer.SerializeToElement("untitled-cache");
            value["session"] = JsonSerializer.SerializeToElement(Session);
            value["kind"] = JsonSerializer.SerializeToElement(kind);
            value["sequence"] = JsonSerializer.SerializeToElement(sequence);
            Emit(JsonSerializer.Serialize(value));
        }
        public async Task Ack(int expected) =>
            Assert.Equal(expected, await _acks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)));
        public Task ExecuteScriptAsync(string script)
        {
            if (script.Contains("window.spectralisCacheUntitled("))
            {
                Session = Regex.Match(script, "window\\.spectralisCacheUntitled\\([^,]+, \"([a-f0-9]+)\"\\);").Groups[1].Value;
                Assert.NotEmpty(Session);
                var root = Path.Combine(Path.GetTempPath(), "Spectralis", "remote-audio");
                _before = Directory.Exists(root) ? Directory.GetFiles(root).ToHashSet() : [];
            }
            else
            {
                var seq = int.Parse(Regex.Match(script, ", ([0-9]+)\\);").Groups[1].Value);
                if (seq == 0)
                {
                    var root = Path.Combine(Path.GetTempPath(), "Spectralis", "remote-audio");
                    CachePath = Directory.GetFiles(root).Except(_before).Single();
                }
                _acks.Writer.TryWrite(seq);
            }
            return Task.CompletedTask;
        }
        public void MapVirtualHost(string hostname, string folderPath) { }
        public void Navigate(Uri url) { }
        public void NavigateToString(string html) { }
        public void NudgeResize() { }
        public void Dispose() { }
    }
}
