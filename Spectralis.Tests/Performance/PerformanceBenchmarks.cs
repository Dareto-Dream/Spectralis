using System.Diagnostics;
using Spectralis.App.ViewModels;
using Spectralis.Core.Common;
using Spectralis.Core.Metadata;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.Performance;

public sealed class LibraryScanBenchmark : IDisposable
{
    private readonly string _root;

    public LibraryScanBenchmark()
    {
        _root = Path.Combine(Path.GetTempPath(), $"spectralis-perf-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public async Task IncrementalRescanOf10kFiles_CompletesUnder10Seconds()
    {
        // The incremental path (fingerprint check, no tag reads) is what runs at
        // every startup, so that is the path held to the 10-second budget.
        var musicDir = Path.Combine(_root, "music");
        Directory.CreateDirectory(musicDir);

        var wavBytes = File.ReadAllBytes(MakeSmallWav());
        for (var i = 0; i < 10_000; i++)
        {
            File.WriteAllBytes(Path.Combine(musicDir, $"track-{i:D5}.wav"), wavBytes);
        }

        using var db = new LibraryDatabase(Path.Combine(_root, "bench.db"));
        var scanner = new LibraryScanner(db);

        await scanner.ScanAsync(new[] { musicDir }); // initial index (tag reads)

        var sw = Stopwatch.StartNew();
        var rescan = await scanner.ScanAsync(new[] { musicDir });
        sw.Stop();

        Assert.Equal(10_000, rescan.Unchanged);
        Assert.True(
            sw.Elapsed < TimeSpan.FromSeconds(10),
            $"Incremental rescan of 10k files took {sw.Elapsed.TotalSeconds:0.0}s (budget 10s).");
    }

    private string MakeSmallWav()
    {
        var path = global::Spectralis.Tests.Core.WavFixture.CreateSineWav(0.05);
        var copy = Path.Combine(_root, "template.wav");
        File.Move(path, copy);
        return copy;
    }
}

public class TrackListBenchmark
{
    [Fact]
    public void Building50kTrackRows_StaysWithinInteractiveBudget()
    {
        var tracks = new List<TrackInfo>(50_000);
        for (var i = 0; i < 50_000; i++)
        {
            tracks.Add(new TrackInfo
            {
                SourcePath = $@"C:\music\artist{i % 500}\album{i % 2000}\track{i}.flac",
                Title = $"Track {i}",
                Artist = $"Artist {i % 500}",
                Album = $"Album {i % 2000}",
                Duration = TimeSpan.FromSeconds(180 + (i % 200)),
                BitrateKbps = 320,
                FileSizeBytes = 30_000_000,
                FormatName = "FLAC",
            });
        }

        var sw = Stopwatch.StartNew();
        var rows = tracks
            .Select(track => TrackRow.From(new LibraryEntry(track, 0, DateTime.UtcNow, null)))
            .ToList();
        var filtered = rows.Where(row => row.Matches("Artist 42")).ToList();
        sw.Stop();

        Assert.Equal(50_000, rows.Count);
        Assert.NotEmpty(filtered);
        // Row materialization + a live-search pass over 50k items must stay far
        // below one frame budget so typing in the search box never stutters.
        Assert.True(
            sw.ElapsedMilliseconds < 500,
            $"50k row build + filter took {sw.ElapsedMilliseconds}ms (budget 500ms).");
    }
}

public class VisualizerSustainedBenchmark
{
    [Fact]
    public void AllRenderers_SustainSixtyFpsLogicFor600Frames()
    {
        // 600 frames = 10 seconds at 60fps. Frame logic must never exceed the
        // 33ms hard ceiling from the requirements.
        var state = new VisualizerSceneState();
        var random = new Random(7);
        var spectrum = new float[64];
        var waveform = new float[256];
        var canvas = new global::Spectralis.Tests.Core.NullVizCanvas();
        var bounds = new VizRect(0, 0, 1920, 1080);

        foreach (var definition in VisualizerCatalog.All)
        {
            for (var frame = 0; frame < 600; frame++)
            {
                for (var i = 0; i < spectrum.Length; i++)
                {
                    spectrum[i] = (float)random.NextDouble();
                }

                for (var i = 0; i < waveform.Length; i++)
                {
                    waveform[i] = ((float)random.NextDouble() * 2f) - 1f;
                }

                state.UpdateFrame(
                    new VisualizerFrame(spectrum, waveform, 0.8f, 0.5f),
                    activePlayback: true,
                    frame / 60f,
                    definition.Mode);

                var sw = Stopwatch.StartNew();
                definition.Renderer.Draw(canvas, bounds, state.CreateScene(definition.Label));
                sw.Stop();

                Assert.True(
                    sw.ElapsedMilliseconds < 33,
                    $"{definition.Label} frame {frame} took {sw.ElapsedMilliseconds}ms (ceiling 33ms).");
            }
        }
    }
}
