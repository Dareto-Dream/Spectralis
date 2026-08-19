using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SharedPlayJoinedPackageStoreTests : IDisposable
{
    private readonly string _cacheRoot;

    public SharedPlayJoinedPackageStoreTests()
    {
        _cacheRoot = Path.Combine(Path.GetTempPath(), $"sp-joined-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { Directory.Delete(_cacheRoot, recursive: true); } catch { }
    }

    private static SharedPlayJoinedSession MakeSession(string roomCode = "ROOM01", string trackId = "track-1") => new(
        roomCode,
        trackId,
        new Uri("https://cdn.example.com/state"),
        new Uri("https://cdn.example.com/queue"),
        new Uri("https://cdn.example.com/package.zip"),
        null);

    [Fact]
    public async Task GetOrDownloadAudioAsync_ExistingCachedAudio_ReturnsItWithoutDownloading()
    {
        var store = new SharedPlayJoinedPackageStore(_cacheRoot);
        var session = MakeSession();

        var audioDir = Path.Combine(_cacheRoot, session.RoomCode, session.TrackId!, "audio");
        Directory.CreateDirectory(audioDir);
        var cachedPath = Path.Combine(audioDir, "track.mp3");
        await File.WriteAllTextAsync(cachedPath, "already downloaded");

        // A real (never-invoked) client is safe here: the cache-hit path must return
        // before touching the network at all.
        using var cdnClient = new SharedPlayCdnClient();

        var result = await store.GetOrDownloadAudioAsync(session, cdnClient, CancellationToken.None);

        Assert.Equal(cachedPath, result);
    }

    [Fact]
    public async Task GetOrDownloadAudioAsync_DifferentTracks_UseSeparateCacheDirectories()
    {
        var store = new SharedPlayJoinedPackageStore(_cacheRoot);
        var sessionA = MakeSession(trackId: "track-a");
        var sessionB = MakeSession(trackId: "track-b");

        foreach (var (session, contents) in new[] { (sessionA, "audio a"), (sessionB, "audio b") })
        {
            var dir = Path.Combine(_cacheRoot, session.RoomCode, session.TrackId!, "audio");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, "track.mp3"), contents);
        }

        using var cdnClient = new SharedPlayCdnClient();

        var pathA = await store.GetOrDownloadAudioAsync(sessionA, cdnClient, CancellationToken.None);
        var pathB = await store.GetOrDownloadAudioAsync(sessionB, cdnClient, CancellationToken.None);

        Assert.NotEqual(pathA, pathB);
        Assert.Equal("audio a", await File.ReadAllTextAsync(pathA));
        Assert.Equal("audio b", await File.ReadAllTextAsync(pathB));
    }

    [Fact]
    public void Clear_RemovesEverythingUnderCacheRoot()
    {
        var store = new SharedPlayJoinedPackageStore(_cacheRoot);
        var dir = Path.Combine(_cacheRoot, "ROOM01", "track-1", "audio");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "track.mp3"), "x");

        store.Clear();

        Assert.Empty(Directory.EnumerateDirectories(_cacheRoot));
    }
}
