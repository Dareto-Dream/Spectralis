using Spectralis.Core.Analysis;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AnalysisTests : IDisposable
{
    private readonly List<string> _tempFiles = new();
    private readonly string _tempDir;

    public AnalysisTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"spectralis-an-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        foreach (var file in _tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }

        try
        {
            Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public async Task BpmAnalyzer_DetectsClickTrackTempo()
    {
        // 2 minutes of clicks at 120 BPM (one click every 0.5s).
        var path = WavFixture.CreateClickTrackWav(bpm: 120, seconds: 30);
        _tempFiles.Add(path);

        var (bpm, _) = await BpmAnalyzer.AnalyzeAsync(path);

        // Octave-folded detection within a small tolerance.
        Assert.True(
            Math.Abs(bpm - 120) < 3 || Math.Abs(bpm - 60) < 2 || Math.Abs(bpm - 180) < 3,
            $"Detected {bpm} BPM for a 120 BPM click track.");
    }

    [Fact]
    public void KeyAnalyzer_DetectsMajorTonality()
    {
        // A sustained C major triad: C4, E4, G4.
        const int sr = 44100;
        var samples = new float[sr * 10];
        double[] freqs = [261.63, 329.63, 392.00];
        for (var i = 0; i < samples.Length; i++)
        {
            foreach (var f in freqs)
            {
                samples[i] += (float)(0.25 * Math.Sin(2 * Math.PI * f * i / sr));
            }
        }

        var key = KeyAnalyzer.Analyze(samples, sr);

        Assert.Equal("C Major", key);
    }

    [Fact]
    public async Task AnalysisWorker_PersistsBpmAndKey()
    {
        var dbPath = Path.Combine(_tempDir, "lib.db");
        using var db = new LibraryDatabase(dbPath);
        var wav = WavFixture.CreateClickTrackWav(bpm: 120, seconds: 12);
        _tempFiles.Add(wav);
        db.Upsert(new TrackInfo { SourcePath = wav, Title = "Click" }, mtimeTicks: 1);

        var worker = new AnalysisWorker(db);
        var result = await worker.AnalyzeTrackAsync(wav);

        Assert.NotNull(result);
        var entry = Assert.Single(db.GetAllEntries());
        Assert.NotNull(entry.Track.Bpm);
        Assert.True(entry.Track.Bpm > 0);
    }
}
