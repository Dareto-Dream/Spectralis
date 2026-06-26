using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class LibraryDatabaseTests : IDisposable
{
    private readonly string _dbPath;
    private readonly LibraryDatabase _db;

    public LibraryDatabaseTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"spectralis-lib-{Guid.NewGuid():N}.db");
        _db = new LibraryDatabase(_dbPath);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { File.Delete(_dbPath); } catch { }
    }

    private static TrackInfo MakeTrack(string path, string title = "Song", string artist = "Artist") =>
        new()
        {
            SourcePath = path,
            Title = title,
            Artist = artist,
            Album = "Album",
            Duration = TimeSpan.FromSeconds(180),
            BitrateKbps = 320,
            SampleRateHz = 44100,
            Channels = 2,
            FileSizeBytes = 1024,
            FormatName = "MP3",
            Bpm = 128,
        };

    [Fact]
    public void UpsertAndReadBack_RoundTripsAllFields()
    {
        _db.Upsert(MakeTrack(@"C:\music\a.mp3"), mtimeTicks: 42);

        var tracks = _db.GetAllTracks();
        var track = Assert.Single(tracks);
        Assert.Equal("Song", track.Title);
        Assert.Equal("Artist", track.Artist);
        Assert.Equal(TimeSpan.FromSeconds(180), track.Duration);
        Assert.Equal(320, track.BitrateKbps);
        Assert.Equal(128, track.Bpm);
        Assert.Equal("MP3", track.FormatName);
    }

    [Fact]
    public void Upsert_SamePathUpdatesInsteadOfDuplicating()
    {
        _db.Upsert(MakeTrack(@"C:\music\a.mp3", "Old"), 1);
        _db.Upsert(MakeTrack(@"C:\music\a.mp3", "New"), 2);

        var track = Assert.Single(_db.GetAllTracks());
        Assert.Equal("New", track.Title);
        Assert.Equal((2L, 1024L), _db.GetFingerprint(@"C:\music\a.mp3"));
    }

    [Fact]
    public void GetFingerprint_MissingPathReturnsNull()
    {
        Assert.Null(_db.GetFingerprint(@"C:\nope.mp3"));
    }

    [Fact]
    public void MarkMissing_ExcludesFromDefaultQueries()
    {
        _db.Upsert(MakeTrack(@"C:\music\a.mp3"), 1);
        _db.MarkMissing(@"C:\music\a.mp3");

        Assert.Empty(_db.GetAllTracks());
        Assert.Single(_db.GetAllTracks(includeMissing: true));
        Assert.Equal(0, _db.Count());
    }

    [Fact]
    public void Remove_DeletesRow()
    {
        _db.Upsert(MakeTrack(@"C:\music\a.mp3"), 1);
        _db.Remove(@"C:\music\a.mp3");
        Assert.Empty(_db.GetAllTracks(includeMissing: true));
    }
}

public sealed class LibraryScannerTests : IDisposable
{
    private readonly string _root;
    private readonly string _dbPath;
    private readonly LibraryDatabase _db;
    private readonly LibraryScanner _scanner;

    public LibraryScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectralis-scan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "library.db");
        _db = new LibraryDatabase(_dbPath);
        _scanner = new LibraryScanner(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MusicDir(string name = "music")
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task Scan_IndexesValidSkipsUnsupportedSurvivesMalformed()
    {
        var dir = MusicDir();
        var nested = Path.Combine(dir, "nested");
        Directory.CreateDirectory(nested);

        File.Copy(WavFixture.CreateSineWav(0.2), Path.Combine(dir, "one.wav"));
        File.Copy(WavFixture.CreateSineWav(0.2), Path.Combine(nested, "two.wav"));
        File.WriteAllBytes(Path.Combine(dir, "garbage.mp3"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(dir, "empty.flac"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "not audio");

        var result = await _scanner.ScanAsync(new[] { dir });

        // Malformed audio files still index (file-name fallback); .txt is ignored.
        Assert.Equal(4, result.Added);
        Assert.Equal(0, result.Failed);
        Assert.Equal(4, _db.Count());
    }

    [Fact]
    public async Task Rescan_SkipsUnchangedFiles()
    {
        var dir = MusicDir();
        File.Copy(WavFixture.CreateSineWav(0.2), Path.Combine(dir, "one.wav"));

        var first = await _scanner.ScanAsync(new[] { dir });
        var second = await _scanner.ScanAsync(new[] { dir });

        Assert.Equal(1, first.Added);
        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Unchanged);
    }

    [Fact]
    public async Task Rescan_DetectsModifiedFiles()
    {
        var dir = MusicDir();
        var path = Path.Combine(dir, "one.wav");
        File.Copy(WavFixture.CreateSineWav(0.2), path);
        await _scanner.ScanAsync(new[] { dir });

        File.Copy(WavFixture.CreateSineWav(0.4), path, overwrite: true);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        var result = await _scanner.ScanAsync(new[] { dir });
        Assert.Equal(1, result.Updated);
    }

    [Fact]
    public async Task Rescan_MarksDeletedFilesMissing()
    {
        var dir = MusicDir();
        var path = Path.Combine(dir, "one.wav");
        File.Copy(WavFixture.CreateSineWav(0.2), path);
        await _scanner.ScanAsync(new[] { dir });

        File.Delete(path);
        var result = await _scanner.ScanAsync(new[] { dir });

        Assert.Equal(1, result.MarkedMissing);
        Assert.Equal(0, _db.Count());
    }

    [Fact]
    public async Task Scan_ReportsProgressAndCompletion()
    {
        var dir = MusicDir();
        File.Copy(WavFixture.CreateSineWav(0.2), Path.Combine(dir, "one.wav"));

        var reports = new List<LibraryScanProgress>();
        var progress = new SynchronousProgress(reports.Add);
        await _scanner.ScanAsync(new[] { dir }, progress);

        Assert.Contains(reports, report => report.Completed);
        Assert.Contains(reports, report => report.Total == 1);
    }

    [Fact]
    public async Task Scan_CanBeCancelled()
    {
        var dir = MusicDir();
        for (var i = 0; i < 5; i++)
        {
            File.Copy(WavFixture.CreateSineWav(0.1), Path.Combine(dir, $"track{i}.wav"));
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _scanner.ScanAsync(new[] { dir }, null, cts.Token));
    }

    [Fact]
    public async Task Scan_SingleFilePathIndexesThatFile()
    {
        var dir = MusicDir();
        var path = Path.Combine(dir, "one.wav");
        File.Copy(WavFixture.CreateSineWav(0.2), path);

        var result = await _scanner.ScanAsync(new[] { path });

        Assert.Equal(1, result.Added);
    }

    /// <summary>IProgress that invokes synchronously (no SynchronizationContext races in tests).</summary>
    private sealed class SynchronousProgress : IProgress<LibraryScanProgress>
    {
        private readonly Action<LibraryScanProgress> _handler;
        public SynchronousProgress(Action<LibraryScanProgress> handler) => _handler = handler;
        public void Report(LibraryScanProgress value) => _handler(value);
    }
}
