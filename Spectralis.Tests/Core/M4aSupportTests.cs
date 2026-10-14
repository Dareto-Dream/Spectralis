using System.Reflection;
using Microsoft.Data.Sqlite;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Spectralis.App.VideoExport;
using Spectralis.Core.Analysis;
using Spectralis.Core.Audio;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class M4aSupportTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"spectralis-m4a-{Guid.NewGuid():N}");

    public M4aSupportTests() => Directory.CreateDirectory(_directory);
    public void Dispose()
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_directory, "library.db"),
            Cache = SqliteCacheMode.Shared,
        }.ToString());
        SqliteConnection.ClearPool(connection);
        Directory.Delete(_directory, recursive: true);
    }

    [Theory]
    [InlineData("aac")]
    [InlineData("alac")]
    [InlineData("libopus")]
    public async Task Playback_supports_m4a_codecs_seeking_and_speed_changes(string codec)
    {
        var path = await Encode(codec);
        Assert.True(SupportedAudioFormats.IsSupportedExtension(path));
        var devices = new FakeAudioDeviceEnumerator();
        using var engine = new AudioEngine(devices);
        engine.Load(path);
        Assert.Equal("M4A", engine.CurrentTrack!.FormatName);
        Assert.Equal(48000, engine.CurrentTrack.SampleRateHz);
        Assert.Equal(2, engine.CurrentTrack.Channels);
        Assert.InRange(engine.GetLength(), 2.9f, 3.1f);

        engine.Play();
        AssertAudible(devices.Current!.Pump(24000));
        engine.Seek(1f);
        Assert.InRange(engine.GetPosition(), 0.999f, 1.001f);
        engine.SetPlaybackRate(1.25);
        AssertAudible(devices.Current!.Pump(24000));
        engine.Pause();
        Assert.Equal(Spectralis.Core.Audio.PlaybackState.Paused, engine.StateMachine.State);
        engine.Stop();
        Assert.Equal(0f, engine.GetPosition());
    }

    [Theory]
    [InlineData("aac")]
    [InlineData("alac")]
    [InlineData("libopus")]
    public async Task Prepared_m4a_crossfades_without_restarting_the_output_device(string codec)
    {
        var first = await Encode(codec);
        var second = await Encode(codec);
        var devices = new FakeAudioDeviceEnumerator();
        using var engine = new AudioEngine(devices) { CrossfadeSeconds = 0.5 };
        string? transitioned = null;
        engine.TrackTransitioned += (_, path) => transitioned = path;
        engine.Load(first);
        engine.Play();
        await engine.PrepareNextAsync(second);
        var device = devices.Current!;
        for (var i = 0; i < 280 && transitioned is null; i++)
            Assert.NotEmpty(device.Pump(1024));

        Assert.Equal(second, transitioned);
        Assert.Single(devices.CreatedDevices);
        Assert.True(device.IsPlaying);
        Assert.True(engine.TrySeamlessAdvance(second));
        AssertAudible(device.Pump(24000));
    }

    [Theory]
    [InlineData("aac")]
    [InlineData("alac")]
    [InlineData("libopus")]
    public async Task Analysis_persists_tempo_key_and_publishes_the_beat_grid(string codec)
    {
        // C major, with a strong attack every half second (120 BPM).
        var path = await Encode(codec,
            @"aevalsrc=(0.18*sin(2*PI*261.63*t)+0.18*sin(2*PI*329.63*t)+0.18*sin(2*PI*392*t))*if(lt(mod(t\,0.5)\,0.08)\,1\,0.1):s=48000:d=10");
        using var db = new LibraryDatabase(Path.Combine(_directory, "library.db"));
        db.Upsert(new TrackInfo { SourcePath = path, Title = "M4A" }, mtimeTicks: 1);
        var worker = new AnalysisWorker(db);
        AnalysisResult? notification = null;
        worker.TrackAnalyzed += (_, result) => notification = result;
        var result = await worker.AnalyzeTrackAsync(path);

        Assert.NotNull(result);
        Assert.Same(result, notification);
        Assert.InRange(result.Bpm, 117f, 123f);
        Assert.Equal("C Major", result.Key);
        Assert.InRange(result.FirstBeatOffset.TotalSeconds, 0, 1);
        var saved = Assert.Single(db.GetAllEntries()).Track;
        Assert.Equal(result.Bpm, saved.Bpm);
        Assert.Equal(result.Key, saved.MusicalKey);
    }

    [Theory]
    [InlineData("aac")]
    [InlineData("alac")]
    [InlineData("libopus")]
    public async Task Video_export_analysis_uses_the_same_m4a_decoder(string codec)
    {
        var path = await Encode(codec);
        // Exercise the actual export entry point without starting Avalonia/GPU rendering.
        var open = typeof(VideoExportEngine).GetMethod("OpenAudioStream", BindingFlags.Static | BindingFlags.NonPublic)!;
        using var stream = (WaveStream)open.Invoke(null, [path, CancellationToken.None])!;
        var samples = new float[24000];
        Assert.Equal(samples.Length, stream.ToSampleProvider().Read(samples, 0, samples.Length));
        AssertAudible(samples);
        Assert.InRange(stream.TotalTime.TotalSeconds, 2.9, 3.1);
    }

    [Fact]
    public async Task Alac_retains_samples_below_16_bit_precision()
    {
        var path = await Encode("alac", "aevalsrc=0.000001|0.000001:s=48000:d=1");
        using var stream = AudioStreamFactory.Open(path);
        Assert.Equal(WaveFormatEncoding.IeeeFloat, stream.WaveFormat.Encoding);
        Assert.Equal(32, stream.WaveFormat.BitsPerSample);
        var samples = new float[1000];
        Assert.Equal(samples.Length, stream.ToSampleProvider().Read(samples, 0, samples.Length));
        Assert.InRange(samples.Min(), 0.0000008f, 0.0000012f);
        Assert.InRange(samples.Max(), 0.0000008f, 0.0000012f);
    }

    [Fact]
    public async Task Container_tracks_are_selected_explicitly_and_can_be_mixed_and_sought_independently()
    {
        var path = Path.Combine(_directory, "mix and stems.M4A");
        await Ffmpeg(["-f", "lavfi", "-i", "aevalsrc=0.1:s=48000:d=2",
            "-f", "lavfi", "-i", "aevalsrc=0.2|0.3:s=48000:d=2",
            "-map", "0:a:0", "-map", "1:a:0", "-c:a", "alac",
            "-disposition:a:0", "0", "-disposition:a:1", "default", "-f", "mp4", path]);

        // FFmpeg's automatic selection prefers the stereo/default track. The factory
        // must open track zero for normal playback and honor explicit stem indexes.
        using var main = AudioStreamFactory.Open(path);
        using var stem = AudioStreamFactory.Open(path, audioStreamIndex: 1);
        Assert.Equal(1, main.WaveFormat.Channels);
        Assert.Equal(2, stem.WaveFormat.Channels);
        main.CurrentTime = TimeSpan.FromSeconds(1);
        Assert.Equal(TimeSpan.Zero, stem.CurrentTime);
        stem.CurrentTime = TimeSpan.FromSeconds(1);
        var mixer = new MixingSampleProvider([
            new MonoToStereoSampleProvider(main.ToSampleProvider()),
            stem.ToSampleProvider()]);
        var output = new float[1000];
        Assert.Equal(output.Length, mixer.Read(output, 0, output.Length));
        for (var i = 0; i < output.Length; i += 2)
        {
            Assert.InRange(output[i], 0.29999f, 0.30001f);
            Assert.InRange(output[i + 1], 0.39999f, 0.40001f);
        }

        Assert.Throws<InvalidOperationException>(() => AudioStreamFactory.Open(path, audioStreamIndex: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => AudioStreamFactory.Open(path, audioStreamIndex: -1));

        var streams = await Spectralis.Core.Audio.Dj.DjTrack.ProbeAsync(path);
        Assert.Equal(new[] { 0, 1 }, streams.Select(s => s.AudioIndex));
        using var dj = new Spectralis.Core.Audio.Dj.DjMixerEngine(new FakeAudioDeviceEnumerator()) { Crossfader = -1, MasterVolume = 1 };
        dj.LoadDeck(0, await Spectralis.Core.Audio.Dj.DjTrack.LoadAsync([new(path, "Stereo stem", 1)]));
        dj.Transport(0, Spectralis.Core.Audio.Dj.DjTransport.PlayPause);
        dj.Read(output, 0, output.Length);
        Assert.InRange(output[^2], .19999f, .20001f);
        Assert.InRange(output[^1], .29999f, .30001f);
    }

    [Fact]
    public async Task Cancellation_propagates_instead_of_becoming_an_unreadable_track()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var path = Path.Combine(_directory, "cancelled.m4a");
        Assert.Throws<OperationCanceledException>(() => AudioStreamFactory.Open(path, cancellationToken: cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => KeyAnalyzer.AnalyzeFile(path, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BpmAnalyzer.AnalyzeAsync(path, cancellation.Token));
    }

    private async Task<string> Encode(string codec, string source = "sine=frequency=440:sample_rate=48000:duration=3")
    {
        var path = Path.Combine(_directory, $"{codec}-{Guid.NewGuid():N}.m4a");
        var arguments = new List<string> { "-f", "lavfi", "-i", source, "-ac", "2", "-c:a", codec };
        if (codec == "alac") arguments.AddRange(["-sample_fmt", "s32p"]);
        arguments.AddRange(["-f", "mp4", path]);
        await Ffmpeg(arguments);
        return path;
    }

    private static async Task Ffmpeg(IReadOnlyList<string> arguments)
    {
        var executable = FfmpegLocator.FindExecutable();
        Assert.NotNull(executable);
        var result = await SafeProcessRunner.RunAsync(executable!,
            ["-hide_banner", "-loglevel", "error", "-nostdin", "-y", .. arguments], TimeSpan.FromSeconds(30));
        Assert.True(result.Succeeded, result.Stderr);
    }

    private static void AssertAudible(float[] samples)
    {
        Assert.NotEmpty(samples);
        Assert.All(samples, sample => Assert.True(float.IsFinite(sample)));
        Assert.Contains(samples, sample => Math.Abs(sample) > 0.001f);
    }
}
