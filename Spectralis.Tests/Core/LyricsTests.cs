using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Core;

public class LrcParserTests
{
    [Fact]
    public void Parse_BasicLrc_ReadsLinesAndMetadata()
    {
        const string lrc =
            """
            [ti:Test Song]
            [ar:Test Artist]
            [00:01.00]First line
            [00:05.50]Second line
            [01:10.25]Third line
            """;

        var doc = LrcParser.Parse(lrc, "test");

        Assert.NotNull(doc);
        Assert.Equal(3, doc!.Lines.Count);
        Assert.Equal("Test Song", doc.Title);
        Assert.Equal("Test Artist", doc.Artist);
        Assert.Equal(1.0, doc.Lines[0].StartTime, 3);
        Assert.Equal(5.5, doc.Lines[1].StartTime, 3);
        Assert.Equal(70.25, doc.Lines[2].StartTime, 3);
        Assert.Equal("First line", doc.Lines[0].Text);
    }

    [Fact]
    public void Parse_OffsetTag_ShiftsAllLines()
    {
        const string lrc =
            """
            [offset:+2000]
            [00:10.00]Line
            """;

        var doc = LrcParser.Parse(lrc, "test");

        Assert.NotNull(doc);
        Assert.Equal(12.0, doc!.Lines[0].StartTime, 3);
        Assert.Equal(2000, doc.OffsetMilliseconds);
    }

    [Fact]
    public void Parse_MultipleTimestampsOnOneLine_DuplicatesLine()
    {
        var doc = LrcParser.Parse("[00:05.00][01:05.00]Chorus", "test");

        Assert.NotNull(doc);
        Assert.Equal(2, doc!.Lines.Count);
        Assert.All(doc.Lines, line => Assert.Equal("Chorus", line.Text));
        Assert.Equal(5.0, doc.Lines[0].StartTime, 3);
        Assert.Equal(65.0, doc.Lines[1].StartTime, 3);
    }

    [Fact]
    public void Parse_InlineWordTimestamps_ProducesSegments()
    {
        var doc = LrcParser.Parse("[00:10.00]<00:10.00>Hello <00:11.00>world", "test");

        Assert.NotNull(doc);
        var line = Assert.Single(doc!.Lines);
        Assert.Equal("Hello world", line.Text);
        Assert.Equal(2, line.Segments.Count);
        Assert.Equal(10.0, line.Segments[0].StartTime, 3);
        Assert.Equal(11.0, line.Segments[1].StartTime, 3);
        Assert.True(doc.HasWordTimings);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("just plain text\nwith no timestamps")]
    [InlineData("[not-a-tag]")]
    public void Parse_InvalidInput_ReturnsNull(string? input)
    {
        Assert.Null(LrcParser.Parse(input, "test"));
    }

    [Fact]
    public void Parse_MalformedTimestampsAreSkipped()
    {
        const string lrc =
            """
            [99:99.99]Bad seconds
            [00:05.00]Good line
            """;

        var doc = LrcParser.Parse(lrc, "test");

        Assert.NotNull(doc);
        Assert.Single(doc!.Lines);
        Assert.Equal("Good line", doc.Lines[0].Text);
    }

    [Fact]
    public void FindLineIndex_BinarySearchBoundaries()
    {
        var doc = LrcParser.Parse("[00:01.00]A\n[00:05.00]B\n[00:10.00]C", "test")!;

        Assert.Equal(-1, doc.FindLineIndex(0.5));
        Assert.Equal(0, doc.FindLineIndex(1.0));
        Assert.Equal(0, doc.FindLineIndex(4.99));
        Assert.Equal(1, doc.FindLineIndex(5.0));
        Assert.Equal(2, doc.FindLineIndex(999));
    }
}

public class LyricsExplanationParserTests
{
    [Fact]
    public void Parse_ValidJson_ReadsTimestampKeys()
    {
        var map = LyricsExplanationParser.Parse("""{"00:05.00":"a note","01:10.25":"another"}""");

        Assert.Equal(2, map.Count);
        Assert.Equal("a note", map["00:05.00"]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    public void Parse_InvalidJson_ReturnsEmpty(string? input)
    {
        Assert.Empty(LyricsExplanationParser.Parse(input));
    }

    [Fact]
    public void GetExplanationForTimestamp_MatchesFormattedKey()
    {
        var map = LyricsExplanationParser.Parse("""{"00:05.50":"note"}""");

        Assert.Equal("note", LyricsExplanationParser.GetExplanationForTimestamp(map, 5.5));
        Assert.Null(LyricsExplanationParser.GetExplanationForTimestamp(map, 6.0));
    }
}

public sealed class LyricsLoaderTests : IDisposable
{
    private readonly string _dir;

    public LyricsLoaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spectralis-lyrics-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    [Fact]
    public void LoadForTrack_FindsLrcSidecar()
    {
        var audio = Path.Combine(_dir, "song.mp3");
        File.WriteAllBytes(audio, new byte[] { 1 });
        File.WriteAllText(Path.Combine(_dir, "song.lrc"), "[00:01.00]Hello");

        var doc = LyricsLoader.LoadForTrack(audio);

        Assert.NotNull(doc);
        Assert.Equal("LRC sidecar", doc!.SourceLabel);
        Assert.Equal("Hello", doc.Lines[0].Text);
    }

    [Fact]
    public void LoadForTrack_AttachesAnnotationsFromJsonSidecar()
    {
        var audio = Path.Combine(_dir, "song.mp3");
        File.WriteAllBytes(audio, new byte[] { 1 });
        File.WriteAllText(Path.Combine(_dir, "song.lrc"), "[00:01.00]Hello\n[00:02.00]World");
        File.WriteAllText(Path.Combine(_dir, "song.lrc.json"), """{"00:01.00":"a greeting"}""");

        var doc = LyricsLoader.LoadForTrack(audio);

        Assert.NotNull(doc);
        Assert.Equal("a greeting", doc!.Lines[0].Explanation);
        Assert.Null(doc.Lines[1].Explanation);
    }

    [Fact]
    public void LoadForTrack_NoSidecarNoEmbedded_ReturnsNull()
    {
        var audio = Path.Combine(_dir, "song.mp3");
        File.WriteAllBytes(audio, new byte[] { 1 });

        Assert.Null(LyricsLoader.LoadForTrack(audio));
    }

    [Fact]
    public void LoadForTrack_MissingFileDoesNotThrow()
    {
        Assert.Null(LyricsLoader.LoadForTrack(Path.Combine(_dir, "missing.mp3")));
    }
}
