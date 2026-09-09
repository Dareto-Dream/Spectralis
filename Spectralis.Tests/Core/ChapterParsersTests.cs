using System.Buffers.Binary;
using System.Text;
using Spectralis.Core.Common;
using Spectralis.Core.Podcasts;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class ChapterParsersTests
{
    [Fact]
    public void CueSheet_ParsesTrackTitlesAndIndexTimes()
    {
        const string cue = """
            FILE "episode.mp3" MP3
              TRACK 01 AUDIO
                TITLE "Intro"
                INDEX 01 00:00:00
              TRACK 02 AUDIO
                TITLE "Main topic"
                INDEX 01 01:30:37
              TRACK 03 AUDIO
                TITLE "Wrap up"
                INDEX 01 45:00:00
            """;

        var chapters = CueSheetParser.Parse(cue);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(TimeSpan.Zero, chapters[0].Start);
        Assert.Equal("Main topic", chapters[1].Title);
        Assert.Equal(90 + (37 / 75.0), chapters[1].Start.TotalSeconds, 3);
        Assert.Equal(TimeSpan.FromMinutes(45), chapters[2].Start);
    }

    [Fact]
    public void Podlove_ParsesNumericStartTimes()
    {
        const string json = """
            { "version": "1.2.0", "chapters": [
                { "startTime": 0, "title": "Intro" },
                { "startTime": 65.5, "title": "Topic", "url": "https://example.com" }
            ] }
            """;

        var chapters = PodloveChapterParser.Parse(json);

        Assert.Equal(2, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal(65.5, chapters[1].Start.TotalSeconds, 3);
        Assert.Equal("https://example.com", chapters[1].Url);
    }

    [Fact]
    public void Podlove_ParsesTimecodeStartStrings()
    {
        const string json = """
            { "chapters": [
                { "start": "00:00:00.000", "title": "A" },
                { "start": "00:01:05", "title": "B" },
                { "start": "1:02:03.5", "title": "C" }
            ] }
            """;

        var chapters = PodloveChapterParser.Parse(json);

        Assert.Equal(3, chapters.Count);
        Assert.Equal(65, chapters[1].Start.TotalSeconds, 3);
        Assert.Equal(3723.5, chapters[2].Start.TotalSeconds, 3);
    }

    [Fact]
    public void Mp4Chpl_ParsesNeroChapterList()
    {
        var payload = BuildChpl(
            (TimeSpan.Zero, "Opening"),
            (TimeSpan.FromSeconds(60), "Segment two"),
            (TimeSpan.FromMinutes(5), "Closing"));

        var chapters = Mp4ChapterReader.ParseChpl(payload);

        Assert.Equal(3, chapters.Count);
        Assert.Equal("Opening", chapters[0].Title);
        Assert.Equal(TimeSpan.FromSeconds(60), chapters[1].Start);
        Assert.Equal("Closing", chapters[2].Title);
    }

    [Fact]
    public void ChapterList_FindActiveIndex_HandlesBoundaries()
    {
        var list = new ChapterList(
            new[]
            {
                new Chapter(TimeSpan.Zero, TimeSpan.FromSeconds(30), "A"),
                new Chapter(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(90), "B"),
                new Chapter(TimeSpan.FromSeconds(90), null, "C"),
            },
            "test");

        Assert.Equal(0, list.FindActiveIndex(0));
        Assert.Equal(0, list.FindActiveIndex(29.9));
        Assert.Equal(1, list.FindActiveIndex(30));
        Assert.Equal(2, list.FindActiveIndex(1000));
        Assert.Equal(-1, ChapterList.Empty.FindActiveIndex(5));
    }

    [Theory]
    [InlineData("show.m4b", "Music", true)]
    [InlineData("ep1.mp3", "Podcast", true)]
    [InlineData("ep2.mp3", "True Crime Audiobook", true)]
    [InlineData("track.flac", "Rock", false)]
    public void PodcastDetector_UsesExtensionAndGenre(string file, string genre, bool expected)
    {
        var track = new TrackInfo { SourcePath = file, Genre = genre };
        Assert.Equal(expected, PodcastDetector.DetectAuto(track, file, podcastFolders: null));
    }

    private static byte[] BuildChpl(params (TimeSpan Start, string Title)[] chapters)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(1);                    // version
        ms.Write(new byte[3]);              // flags
        ms.Write(new byte[4]);              // Nero reserved
        ms.WriteByte((byte)chapters.Length);

        var ticks = new byte[8];
        foreach (var (start, title) in chapters)
        {
            BinaryPrimitives.WriteUInt64BigEndian(ticks, (ulong)start.Ticks);
            ms.Write(ticks);
            var titleBytes = Encoding.UTF8.GetBytes(title);
            ms.WriteByte((byte)titleBytes.Length);
            ms.Write(titleBytes);
        }

        return ms.ToArray();
    }
}
