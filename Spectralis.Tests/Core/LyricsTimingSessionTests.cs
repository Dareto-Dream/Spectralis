using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Core;

public class LyricsTimingSessionTests
{
    private static LyricsTimingSession LoadedSession()
    {
        var session = new LyricsTimingSession();
        session.LoadPlainText("First line\n\nSecond line\r\nThird line\n   ");
        return session;
    }

    [Fact]
    public void LoadPlainText_SkipsBlankLines()
    {
        var session = LoadedSession();
        Assert.Equal(3, session.Lines.Count);
        Assert.Equal("First line", session.Lines[0].Text);
        Assert.Equal(0, session.CurrentIndex);
    }

    [Fact]
    public void Tap_StampsAndAdvances()
    {
        var session = LoadedSession();

        Assert.Equal(0, session.Tap(1.5));
        Assert.Equal(1, session.Tap(4.25));
        Assert.Equal(1.5, session.Lines[0].Timestamp);
        Assert.Equal(4.25, session.Lines[1].Timestamp);
        Assert.Equal(2, session.CurrentIndex);
        Assert.False(session.IsComplete);

        session.Tap(8.0);
        Assert.True(session.IsComplete);
        Assert.Equal(-1, session.Tap(99)); // taps past the end are ignored
    }

    [Fact]
    public void UndoLastTap_StepsBackAndClears()
    {
        var session = LoadedSession();
        session.Tap(1.0);
        session.Tap(2.0);

        Assert.True(session.UndoLastTap());
        Assert.Equal(1, session.CurrentIndex);
        Assert.Null(session.Lines[1].Timestamp);
        Assert.Equal(1.0, session.Lines[0].Timestamp);
    }

    [Fact]
    public void ExportLrc_RoundTripsThroughParser()
    {
        var session = LoadedSession();
        session.Tap(1.5);
        session.Tap(64.27);
        session.Tap(125.0);

        var lrc = session.ExportLrc(title: "Song", artist: "Artist");
        var parsed = LrcParser.Parse(lrc, "round-trip");

        Assert.NotNull(parsed);
        Assert.Equal(3, parsed!.Lines.Count);
        Assert.Equal("Song", parsed.Title);
        Assert.Equal("Artist", parsed.Artist);
        Assert.Equal(1.5, parsed.Lines[0].StartTime, 2);
        Assert.Equal(64.27, parsed.Lines[1].StartTime, 2);
        Assert.Equal("Second line", parsed.Lines[1].Text);
    }

    [Fact]
    public void ExportLrc_OmitsUntimedLines()
    {
        var session = LoadedSession();
        session.Tap(1.0); // only the first line stamped

        var parsed = LrcParser.Parse(session.ExportLrc(), "partial");

        Assert.NotNull(parsed);
        Assert.Single(parsed!.Lines);
    }

    [Fact]
    public void AdjustTimestamp_OnlyTouchesStampedLines()
    {
        var session = LoadedSession();
        session.Tap(5.0);

        session.AdjustTimestamp(0, 4.2);
        session.AdjustTimestamp(1, 9.9); // untimed: ignored
        session.AdjustTimestamp(0, -3);  // clamped

        Assert.Equal(0, session.Lines[0].Timestamp);
        Assert.Null(session.Lines[1].Timestamp);
    }

    [Fact]
    public void SaveSidecar_WritesLrcNextToAudio()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"spectralis-timing-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var audio = Path.Combine(dir, "song.mp3");
            File.WriteAllBytes(audio, new byte[] { 1 });
            var session = LoadedSession();
            session.Tap(2.0);

            var path = session.SaveSidecar(audio);

            Assert.Equal(Path.Combine(dir, "song.lrc"), path);
            Assert.NotNull(LyricsLoader.LoadSidecar(audio));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void FormatTimestamp_CentisecondPrecision()
    {
        Assert.Equal("[00:01.50]", LyricsTimingSession.FormatTimestamp(1.5));
        Assert.Equal("[02:05.25]", LyricsTimingSession.FormatTimestamp(125.25));
        Assert.Equal("[00:00.00]", LyricsTimingSession.FormatTimestamp(0));
    }
}
