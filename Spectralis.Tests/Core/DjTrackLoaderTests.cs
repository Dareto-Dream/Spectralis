using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Spectralis.Core.Audio.Dj;
using Spectralis.Core.Integrations;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class DjTrackLoaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "spectralis-detect-test-" + Guid.NewGuid().ToString("N"));
    public DjTrackLoaderTests() => Directory.CreateDirectory(_directory);
    public void Dispose() => Directory.Delete(_directory, true);

    [Fact]
    public async Task Multiple_selected_files_are_stems_in_selection_order_with_their_filenames()
    {
        var vocals = await Container("Song - Lead Vocals.m4a", ["Shared song title"]);
        var drums = await Container("Song - Drums.m4a", ["Shared song title"]);
        var inputs = await DjTrackLoader.ResolveAsync([vocals, drums]);
        Assert.Equal(new[] { vocals, drums }, inputs.Select(i => i.Path));
        Assert.Equal(new[] { "Song - Lead Vocals", "Song - Drums" }, inputs.Select(i => i.Name));
        Assert.All(inputs, input => Assert.Equal(0, input.AudioStreamIndex));
    }

    [Fact]
    public async Task Ordinary_file_loads_normally_without_splitting_stereo_channels()
    {
        var path = await Container("Ordinary song.m4a", ["SoundHandler"]);
        var input = Assert.Single(await DjTrackLoader.ResolveAsync([path]));
        Assert.Equal("Ordinary song", input.Name); Assert.Equal(0, input.AudioStreamIndex);
    }

    [Fact]
    public async Task Embedded_stems_keep_unicode_handler_names_and_skip_an_explicit_master()
    {
        var names = new[] { "Master Mix", "Batería: acústica", "Bass", "Lead Vox" };
        var path = await Container("Song.m4a", names);
        var inputs = await DjTrackLoader.ResolveAsync([path]);
        Assert.Equal(names.Skip(1), inputs.Select(i => i.Name));
        Assert.Equal(new[] { 1, 2, 3 }, inputs.Select(i => i.AudioStreamIndex));
    }

    [Fact]
    public async Task Multiple_streams_without_a_master_all_load_even_when_first_is_default()
    {
        var path = await Container("Stem set.mp4", ["Drums", "Master vocals", "Bass"]);
        var inputs = await DjTrackLoader.ResolveAsync([path]);
        Assert.Equal(new[] { "Drums", "Master vocals", "Bass" }, inputs.Select(i => i.Name));
        Assert.Equal(new[] { 0, 1, 2 }, inputs.Select(i => i.AudioStreamIndex));
    }

    [Fact]
    public async Task Native_stem_atom_supplies_original_names_and_excludes_the_full_mix()
    {
        var path = await Container("Native renamed.m4a", Enumerable.Repeat("SoundHandler", 5).ToArray());
        var names = new[] { "Live drums", "Sub-bass", "Guitares", "Voz principal" };
        AddStemAtom(path, JsonSerializer.SerializeToUtf8Bytes(new { version = 1, stems = names.Select(name => new { name }) }));
        var inputs = await DjTrackLoader.ResolveAsync([path]);
        Assert.Equal(names, inputs.Select(i => i.Name));
        Assert.Equal(new[] { 1, 2, 3, 4 }, inputs.Select(i => i.AudioStreamIndex));
    }

    [Fact]
    public async Task Native_extension_with_missing_tags_still_excludes_its_master()
    {
        var path = await Container("Example.stem.mp4", Enumerable.Repeat("SoundHandler", 5).ToArray());
        var inputs = await DjTrackLoader.ResolveAsync([path]);
        Assert.Equal(new[] { 1, 2, 3, 4 }, inputs.Select(i => i.AudioStreamIndex));
        Assert.All(inputs, s => Assert.StartsWith("Example.stem · stem ", s.Name));
    }

    [Fact]
    public async Task Malformed_optional_stem_json_does_not_prevent_loading_named_audio()
    {
        var path = await Container("Malformed.m4a", ["Master", "Drums", "Bass"]);
        AddStemAtom(path, Encoding.UTF8.GetBytes("{not-json}"));
        var inputs = await DjTrackLoader.ResolveAsync([path]);
        Assert.Equal(new[] { "Drums", "Bass" }, inputs.Select(i => i.Name));
        using var track = await DjTrack.LoadAsync(inputs);
        Assert.Equal(2, track.Stems.Count);
    }

    [Fact]
    public void Stream_titles_take_priority_and_video_or_data_tags_never_leak_into_stems()
    {
        var log = """
            Input #0, mov, from 'song.m4a':
              Metadata:
                title: Global song title
              Stream #0:0: Video: mjpeg
                Metadata:
                  title: Cover art
              Stream #0:1(und): Audio: aac, stereo (default)
                Metadata:
                  handler_name: SoundHandler
                  title: Percussion: live
              Stream #0:2: Data: bin_data
                Metadata:
                  title: Private data
              Stream #0:3(eng): Audio: alac, stereo
                Metadata:
                  title: Lead voice
                  handler_name: Lower priority
            At least one output file must be specified
            """;
        var streams = DjTrackLoader.ParseStreams(log, "song.m4a");
        Assert.Equal(new[] { "Percussion: live", "Lead voice" }, streams.Select(s => s.Name));
        Assert.Equal(new[] { 0, 1 }, streams.Select(s => s.AudioIndex));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_metadata_reader_handles_extended_boxes_and_skips_audio_payloads(bool extended)
    {
        var path = Path.Combine(_directory, "boxes.mp4");
        var json = Encoding.UTF8.GetBytes("{\"stems\":[{\"name\":\"Actual name\"}]}");
        File.WriteAllBytes(path, [.. Atom("mdat", Atom("stem", Encoding.UTF8.GetBytes("{\"stems\":[{\"name\":\"Wrong name\"}]}"))),
            .. Atom("moov", Atom("udta", Atom("stem", json, extended), extended), extended)]);
        Assert.Equal(new[] { "Actual name" }, Mp4StemMetadata.ReadNames(path, default));
    }

    [Fact]
    public void Malformed_atom_sizes_and_json_shapes_are_ignored_without_allocating_declared_sizes()
    {
        var path = Path.Combine(_directory, "corrupt.mp4");
        File.WriteAllBytes(path, [0xff, 0xff, 0xff, 0xff, (byte)'m', (byte)'o', (byte)'o', (byte)'v']);
        Assert.Empty(Mp4StemMetadata.ReadNames(path, default));
        File.WriteAllBytes(path, Atom("moov", Atom("udta", Atom("stem", Encoding.UTF8.GetBytes("[]")))));
        Assert.Empty(Mp4StemMetadata.ReadNames(path, default));
    }

    [Fact]
    public async Task Too_many_files_or_embedded_stems_report_an_error_instead_of_silently_truncating()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => DjTrackLoader.ResolveAsync(Enumerable.Repeat("unused", 9).ToArray()));
        var path = await Container("Too many.m4a", Enumerable.Range(1, 9).Select(i => $"Stem {i}").ToArray());
        await Assert.ThrowsAsync<NotSupportedException>(() => DjTrackLoader.ResolveAsync([path]));
    }

    [Fact]
    public async Task Pre_cancelled_detection_propagates_cancellation_before_opening_files()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => DjTrackLoader.ResolveAsync(["unused.m4a"], cancellation.Token));
    }

    private async Task<string> Container(string name, string[] streamNames)
    {
        var path = Path.Combine(_directory, name);
        var arguments = new List<string> { "-hide_banner", "-loglevel", "error", "-nostdin", "-y" };
        for (var i = 0; i < streamNames.Length; i++)
            arguments.AddRange(["-f", "lavfi", "-i", $"aevalsrc=0.{i + 1:00}|0.{i + 1:00}:s=48000:d=1"]);
        for (var i = 0; i < streamNames.Length; i++)
            arguments.AddRange(["-map", $"{i}:a:0", $"-metadata:s:a:{i}", "handler_name=" + streamNames[i]]);
        arguments.AddRange(["-c:a", "alac", "-f", "mp4", path]);
        var result = await SafeProcessRunner.RunAsync(FfmpegLocator.FindExecutable()!, arguments, TimeSpan.FromSeconds(30));
        Assert.True(result.Succeeded, result.Stderr);
        return path;
    }

    private static byte[] Atom(string type, byte[] payload, bool extended = false)
    {
        var headerSize = extended ? 16 : 8;
        var bytes = new byte[payload.Length + headerSize];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, extended ? 1u : (uint)bytes.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(bytes, 4);
        if (extended) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(8), (ulong)bytes.Length);
        payload.CopyTo(bytes, headerSize);
        return bytes;
    }

    private static void AddStemAtom(string path, byte[] json)
    {
        // FFmpeg puts moov last by default. Appending inside it leaves all mdat
        // sample offsets intact and creates the same moov/udta/stem hierarchy.
        var bytes = File.ReadAllBytes(path);
        for (var offset = 0; offset + 8 <= bytes.Length;)
        {
            var size = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset));
            Assert.True(size >= 8);
            if (Encoding.ASCII.GetString(bytes, offset + 4, 4) == "moov")
            {
                Assert.Equal(bytes.Length, offset + size);
                var metadata = Atom("udta", Atom("stem", json));
                BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(offset), (uint)(size + metadata.Length));
                File.WriteAllBytes(path, [.. bytes, .. metadata]);
                return;
            }
            offset += size;
        }
        Assert.Fail("Fixture did not contain a moov atom.");
    }
}
