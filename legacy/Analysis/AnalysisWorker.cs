using NAudio.Wave;

namespace Spectralis;

internal sealed class AnalysisWorker
{
    private readonly MusicLibrary _library;
    private readonly LibraryStore _store;
    private CancellationTokenSource? _cts;

    public event EventHandler<AnalysisResult>? TrackAnalyzed;
    public event EventHandler? Completed;
    public event EventHandler<int>? ProgressChanged;  // pending count remaining

    public bool IsRunning { get; private set; }

    public AnalysisWorker(MusicLibrary library, LibraryStore store)
    {
        _library = library;
        _store   = store;
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Cancel()
    {
        _cts?.Cancel();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var pending = _library.Tracks
                .Where(t => t.Bpm is null && !string.IsNullOrWhiteSpace(t.Path))
                .ToList();

            var remaining = pending.Count;
            ProgressChanged?.Invoke(this, remaining);

            foreach (var track in pending)
            {
                if (ct.IsCancellationRequested) break;
                await AnalyzeTrackAsync(track, ct);
                remaining--;
                ProgressChanged?.Invoke(this, remaining);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            IsRunning = false;
            if (!ct.IsCancellationRequested)
                Completed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task AnalyzeTrackAsync(LibraryTrack track, CancellationToken ct)
    {
        try
        {
            var (bpm, firstBeat) = await BpmAnalyzer.AnalyzeAsync(track.Path, ct);

            // Key analysis: read first 30s mono for chromagram
            var key = await Task.Run(() =>
            {
                try
                {
                    using var reader = new AudioFileReader(track.Path);
                    var sr       = reader.WaveFormat.SampleRate;
                    var ch       = reader.WaveFormat.Channels;
                    var maxRead  = Math.Min(30 * sr * ch, (int)(reader.Length / sizeof(float)));
                    var raw      = new float[maxRead];
                    var totalRead = reader.Read(raw, 0, maxRead);

                    // Mix to mono
                    var monoLen = totalRead / ch;
                    var mono    = new float[monoLen];
                    for (var i = 0; i < monoLen; i++)
                    {
                        var sum = 0f;
                        for (var c = 0; c < ch; c++) sum += raw[i * ch + c];
                        mono[i] = sum / ch;
                    }
                    return KeyAnalyzer.Analyze(mono, sr);
                }
                catch { return ""; }
            }, ct);

            ct.ThrowIfCancellationRequested();

            // Update library in-memory and persist
            var updated = track with { Bpm = bpm, Key = string.IsNullOrWhiteSpace(key) ? null : key };
            _library.UpsertAnalysis(updated);
            _store.UpdateAnalysis(track.Path, bpm, key);

            var result = new AnalysisResult(track.Path, bpm, key, firstBeat);
            TrackAnalyzed?.Invoke(this, result);
        }
        catch (OperationCanceledException) { throw; }
        catch { /* skip on error */ }
    }
}

internal sealed record AnalysisResult(
    string   Path,
    float    Bpm,
    string   Key,
    TimeSpan FirstBeatOffset);
