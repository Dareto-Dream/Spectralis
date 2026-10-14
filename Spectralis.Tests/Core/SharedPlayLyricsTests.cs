using System.Text.Json;
using Spectralis.Core.Lyrics;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SharedPlayLyricsTests
{
    [Fact]
    public void Plain_lrc_exports_lines_without_inventing_word_timing()
    {
        var document = LrcParser.Parse("[00:05]Hello there\n[00:09]Another line", "test")!;
        Assert.False(document.HasWordTimings);
        var lines = SharedPlayCacheStore.BuildSharedPlayLyrics(document);
        Assert.Equal(2, lines.Length);
        Assert.Equal(5, lines[0].TimeSeconds);
        Assert.All(lines, line => Assert.Null(line.Words));
    }

    [Theory]
    [InlineData("[00:05]<00:05>Hello <00:06>there")]
    [InlineData("[00:05]Hello <00:05>there<00:06>")]
    public void Enhanced_lrc_preserves_word_times_text_and_offset(string lrc)
    {
        var document = LrcParser.Parse("[offset:2000]\n" + lrc, "test")!;
        Assert.True(document.HasWordTimings);
        var line = Assert.Single(SharedPlayCacheStore.BuildSharedPlayLyrics(document));
        Assert.Equal(7, line.TimeSeconds);
        Assert.Equal("Hello there", line.Text);
        Assert.Collection(line.Words!,
            word => { Assert.Equal(7, word.TimeSeconds); Assert.Equal("Hello ", word.Text); },
            word => { Assert.Equal(8, word.TimeSeconds); Assert.Equal("there", word.Text); });
    }

    [Fact]
    public void Single_word_inline_timing_survives_repeated_lines_and_offsets()
    {
        var document = LrcParser.Parse("[offset:1000]\n[00:05][00:10]<00:06>Hello", "test")!;
        Assert.True(document.HasWordTimings);
        var lines = SharedPlayCacheStore.BuildSharedPlayLyrics(document);
        Assert.Equal(7, Assert.Single(lines[0].Words!).TimeSeconds);
        Assert.Equal(12, Assert.Single(lines[1].Words!).TimeSeconds);
    }

    [Fact]
    public void Absent_description_and_empty_lyrics_have_no_controls_to_expose()
    {
        Assert.Empty(SharedPlayCacheStore.BuildSharedPlayLyrics(null));
        Assert.Empty(SharedPlayCacheStore.BuildSharedPlayLyrics(LrcParser.ParsePlainText("A description", "test")));
        Assert.Empty(SharedPlayCacheStore.BuildSharedPlayLyrics(LrcParser.Parse("[00:01]   ", "test")));
    }

    [Fact]
    public void Word_times_roundtrip_while_old_line_only_manifests_still_deserialize()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var legacy = JsonSerializer.Deserialize<SharedPlayLyricLine>("""{"timeSeconds":5,"text":"Hello"}""", options)!;
        Assert.Null(legacy.Words);
        var exported = SharedPlayCacheStore.BuildSharedPlayLyrics(LrcParser.Parse("[00:05]<00:05>Hello <00:06>there", "test"));
        var json = JsonSerializer.Serialize(exported, options);
        Assert.Contains("\"words\"", json);
        var restored = JsonSerializer.Deserialize<SharedPlayLyricLine[]>(json, options)!;
        Assert.Equal(exported[0].Words, restored[0].Words);
    }
}
