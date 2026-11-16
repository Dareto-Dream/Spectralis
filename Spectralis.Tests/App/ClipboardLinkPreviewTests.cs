using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public class ClipboardLinkPreviewTests
{
    private const string YouTube = "https://www.youtube.com/watch?v=WmdAk2zyQkU";
    private const string SoundCloud = "https://soundcloud.com/artist/track";

    [Theory]
    [InlineData(YouTube)]
    [InlineData("https://youtu.be/WmdAk2zyQkU?si=share")]
    [InlineData(SoundCloud)]
    [InlineData("https://on.soundcloud.com/share")]
    [InlineData("https://suno.com/song/8ee05968-fb92-400b-b445-9aa50726b89c")]
    [InlineData("https://untitled.stream/library/project/abc")]
    [InlineData("https://www.bandlab.com/track/abc")]
    [InlineData("https://media.example.test/music.flac")]
    [InlineData("spotify:track:6eLyGKZI33EAgaRyWJ6FvH")]
    [InlineData("https://open.spotify.com/track/6eLyGKZI33EAgaRyWJ6FvH")]
    public void RecognizesSupportedSourcesWithoutResolving(string input)
    {
        Assert.True(OpenUrlService.TryCreateClipboardPreview(input, out var preview));
        Assert.True(preview.MetadataOnly);
        Assert.Null(preview.CachedAudioPath);
    }

    [Theory]
    [InlineData("https://example.com/article")]
    [InlineData("https://youtube.com.evil.test/watch?v=WmdAk2zyQkU")]
    [InlineData("https://notsoundcloud.com/artist/track")]
    [InlineData("https://suno.com.evil.test/song/id")]
    [InlineData("https://open.spotify.com/track/not-an-id")]
    [InlineData("https://youtube.com/@channel")]
    [InlineData("file:///Users/me/song.mp3")]
    [InlineData("not a link")]
    [InlineData("Application")]
    [InlineData("helloWorld1")]
    [InlineData("WmdAk2zyQkU")]
    [InlineData("8ee05968-fb92-400b-b445-9aa50726b89c")]
    public void IgnoresOrdinaryLinksAndLookalikeHosts(string input) =>
        Assert.False(OpenUrlService.TryCreateClipboardPreview(input, out _));

    [Fact]
    public async Task ToastShowsBeforeMetadataAndArtworkAndUnchangedClipboardDoesNoWork()
    {
        OpenUrlService.TryCreateClipboardPreview(YouTube, out var preview);
        var metadata = new TaskCompletionSource<RemoteAudioResolveResult>();
        var artwork = new TaskCompletionSource<byte[]?>();
        var fetchingArtwork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new List<string>();
        using var monitor = new ClipboardLinkPreview((_, _) => events.Add("show"),
            (_, result, bytes) => { events.Add(bytes is null ? "metadata" : "artwork"); return true; },
            (_, _) => { events.Add("resolve"); return metadata.Task; },
            (_, _) => { events.Add("fetch-art"); fetchingArtwork.TrySetResult(); return artwork.Task; }, _ => Assert.Fail());
        var pending = monitor.ObserveAsync(YouTube);
        Assert.Equal(new[] { "show", "resolve" }, events);
        await monitor.ObserveAsync(YouTube);
        Assert.Equal(2, events.Count);
        metadata.SetResult(preview with { Title = "Track", ArtworkUrl = "https://images.example.test/art.jpg" });
        await fetchingArtwork.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "show", "resolve", "metadata", "fetch-art" }, events);
        artwork.SetResult([1, 2, 3]);
        await pending;
        Assert.Equal("artwork", events[^1]);
    }

    [Fact]
    public async Task NewCopyCancelsOldMetadataEvenWhenTheOldResolverIgnoresCancellation()
    {
        var slow = new TaskCompletionSource<RemoteAudioResolveResult>();
        var updates = new List<string>();
        CancellationToken firstToken = default;
        using var monitor = new ClipboardLinkPreview((url, _) => updates.Add("show:" + url),
            (url, _, _) => { updates.Add("update:" + url); return true; },
            (url, token) => {
                OpenUrlService.TryCreateClipboardPreview(url, out var preview);
                if (url == YouTube) { firstToken = token; return slow.Task; }
                return Task.FromResult(preview);
            }, (_, _) => throw new Exception("Unexpected artwork"), _ => Assert.Fail());
        var first = monitor.ObserveAsync(YouTube);
        await monitor.ObserveAsync(SoundCloud);
        Assert.True(firstToken.IsCancellationRequested);
        OpenUrlService.TryCreateClipboardPreview(YouTube, out var stale);
        slow.SetResult(stale);
        await first;
        Assert.DoesNotContain("update:" + YouTube, updates);
        Assert.Contains("update:" + SoundCloud, updates);
    }

    [Fact]
    public async Task OrdinaryClipboardTextNeverStartsResolution()
    {
        using var monitor = new ClipboardLinkPreview((_, _) => Assert.Fail(), (_, _, _) => throw new Exception(),
            (_, _) => throw new Exception("Should not resolve"), (_, _) => throw new Exception(), _ => Assert.Fail());
        await monitor.ObserveAsync("https://example.com/news");
    }

    [Fact]
    public async Task FindsSupportedLinksInsidePlainTextAndDoesNotToastTheSameVideoTwice()
    {
        var shown = new List<string>();
        using var monitor = new ClipboardLinkPreview((url, _) => shown.Add(url), (_, _, _) => true,
            (url, _) => { OpenUrlService.TryCreateClipboardPreview(url, out var preview); return Task.FromResult(preview); },
            (_, _) => throw new Exception("Unexpected artwork"), _ => Assert.Fail());
        await monitor.ObserveAsync("Read https://example.com/news then listen to " + YouTube + ". Thanks!");
        Assert.Single(shown);
        Assert.Equal(YouTube, shown[0]);
        await monitor.ObserveAsync("some other text");
        await monitor.ObserveAsync("Same song: https://youtu.be/WmdAk2zyQkU?si=share");
        await monitor.ObserveAsync(YouTube + "&list=RDWmdAk2zyQkU&start_radio=1");
        Assert.Single(shown);
        await monitor.ObserveAsync("Listen to " + SoundCloud + " for something else.");
        Assert.Equal(2, shown.Count);
        await monitor.ObserveAsync(string.Join(' ', Enumerable.Repeat("https://example.com/news", 25)) +
            " a song: https://suno.com/song/8ee05968-fb92-400b-b445-9aa50726b89c");
        Assert.Equal(3, shown.Count);
    }

    [Fact]
    public async Task SpotifyUriAndWebLinkShareTheSameIdentity()
    {
        var shown = 0;
        using var monitor = new ClipboardLinkPreview((_, _) => shown++, (_, _, _) => true,
            (url, _) => { OpenUrlService.TryCreateClipboardPreview(url, out var preview); return Task.FromResult(preview); },
            (_, _) => throw new Exception("Unexpected artwork"), _ => Assert.Fail());
        await monitor.ObserveAsync("Song: spotify:track:6eLyGKZI33EAgaRyWJ6FvH");
        await monitor.ObserveAsync("https://open.spotify.com/track/6eLyGKZI33EAgaRyWJ6FvH?si=share");
        Assert.Equal(1, shown);
    }

    [Fact]
    public async Task DismissingTheToastStopsArtworkAndNeverShowsItAgain()
    {
        OpenUrlService.TryCreateClipboardPreview(YouTube, out var preview);
        var shows = 0;
        using var monitor = new ClipboardLinkPreview((_, _) => shows++, (_, _, _) => false,
            (_, _) => Task.FromResult(preview with { ArtworkUrl = "https://image.test/cover" }),
            (_, _) => throw new Exception("Dismissed toast must not fetch artwork"), _ => Assert.Fail());
        await monitor.ObserveAsync(YouTube);
        Assert.Equal(1, shows);
    }
}
