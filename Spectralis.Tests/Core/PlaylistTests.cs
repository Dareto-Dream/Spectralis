using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Spectralis.Core.Playlists;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class PlaylistTests : IDisposable
{
    private readonly string _tempDir;

    public PlaylistTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectralis-pl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void M3u_ExportImport_RoundTripsItems()
    {
        var path = Path.Combine(_tempDir, "test.m3u8");
        var items = new List<PlaylistItem>
        {
            new() { Path = @"C:\music\one.mp3", Title = "One", Artist = "Artist A", DurationSeconds = 61 },
            new() { Path = @"C:\music\two.flac", Title = "Two", DurationSeconds = 0 },
        };

        M3uParser.Export(path, items);
        var imported = M3uParser.ImportItems(path);

        Assert.Equal(2, imported.Count);
        Assert.Equal(@"C:\music\one.mp3", imported[0].Path);
        Assert.Equal("One", imported[0].Title);
        Assert.Equal("Artist A", imported[0].Artist);
        Assert.Equal(61, imported[0].DurationSeconds);
        Assert.Equal("Two", imported[1].Title);
        Assert.Null(imported[1].Artist);
    }

    [Fact]
    public void M3u_Import_ResolvesRelativePathsAndSkipsComments()
    {
        var path = Path.Combine(_tempDir, "rel.m3u");
        File.WriteAllLines(path, ["#EXTM3U", "# comment", "sub\\song.mp3", @"C:\abs\other.mp3"]);

        var paths = M3uParser.Import(path);

        Assert.Equal(2, paths.Count);
        Assert.Equal(Path.Combine(_tempDir, "sub", "song.mp3"), paths[0]);
        Assert.Equal(@"C:\abs\other.mp3", paths[1]);
    }

    private static LibraryEntry Entry(
        string path,
        string title = "",
        string artist = "",
        string genre = "",
        uint year = 0,
        int playCount = 0,
        DateTime? dateAdded = null) =>
        new(
            new TrackInfo
            {
                SourcePath = path,
                Title = title,
                Artist = artist,
                Genre = genre,
                Year = year,
            },
            playCount,
            dateAdded ?? DateTime.UtcNow,
            null);

    [Fact]
    public void SmartEvaluator_MatchAll_FiltersAndSorts()
    {
        var library = new List<LibraryEntry>
        {
            Entry("a.mp3", title: "Alpha", artist: "Rock Band", genre: "Rock", playCount: 5),
            Entry("b.mp3", title: "Beta", artist: "Rock Band", genre: "Rock", playCount: 9),
            Entry("c.mp3", title: "Gamma", artist: "Jazz Cat", genre: "Jazz", playCount: 2),
        };

        var smart = new SmartPlaylist
        {
            Match = SmartMatchMode.All,
            Rules =
            [
                new SmartRule { Field = SmartRuleField.Genre, Op = SmartRuleOp.Is, Value = "Rock" },
            ],
            SortBy = SmartSortField.PlayCount,
            SortDescending = true,
        };

        var result = SmartPlaylistEvaluator.Evaluate(smart, library);

        Assert.Equal(["b.mp3", "a.mp3"], result);
    }

    [Fact]
    public void SmartEvaluator_MatchAny_AndLimit()
    {
        var library = new List<LibraryEntry>
        {
            Entry("a.mp3", title: "Alpha", year: 2001),
            Entry("b.mp3", title: "Beta", year: 2015),
            Entry("c.mp3", title: "Alphabet", year: 1999),
        };

        var smart = new SmartPlaylist
        {
            Match = SmartMatchMode.Any,
            Rules =
            [
                new SmartRule { Field = SmartRuleField.Title, Op = SmartRuleOp.Contains, Value = "Alpha" },
                new SmartRule { Field = SmartRuleField.Year, Op = SmartRuleOp.GreaterThan, Value = "2010" },
            ],
            SortBy = SmartSortField.Title,
            SortDescending = false,
            Limit = 2,
        };

        var result = SmartPlaylistEvaluator.Evaluate(smart, library);

        Assert.Equal(2, result.Count);
        Assert.Equal(["a.mp3", "c.mp3"], result);
    }

    [Fact]
    public void LibraryDatabase_PlayCountAndDateAdded_RoundTrip()
    {
        var dbPath = Path.Combine(_tempDir, "lib.db");
        using var db = new LibraryDatabase(dbPath);

        db.Upsert(new TrackInfo { SourcePath = @"C:\music\x.mp3", Title = "X" }, mtimeTicks: 1);
        db.IncrementPlayCount(@"C:\music\x.mp3");
        db.IncrementPlayCount(@"C:\music\x.mp3");

        var entries = db.GetAllEntries();

        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.PlayCount);
        Assert.NotNull(entry.LastPlayed);
        Assert.True(entry.DateAdded > DateTime.UtcNow.AddMinutes(-5));

        // Re-upserting must not reset the original date-added stamp.
        var originalAdded = entry.DateAdded;
        db.Upsert(new TrackInfo { SourcePath = @"C:\music\x.mp3", Title = "X2" }, mtimeTicks: 2);
        var updated = Assert.Single(db.GetAllEntries());
        Assert.Equal(originalAdded, updated.DateAdded);
        Assert.Equal(2, updated.PlayCount);
    }
}
