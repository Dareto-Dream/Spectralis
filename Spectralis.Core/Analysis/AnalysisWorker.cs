using Spectralis.Core.Metadata;

namespace Spectralis.Core.Analysis;

public sealed record BeatGrid(
    float Bpm,
    TimeSpan FirstBeatOffset,
    int BeatsPerBar,
    string Key);

public sealed record AnalysisResult(
    string Path,
    float Bpm,
    string Key,
    TimeSpan FirstBeatOffset);

/// <summary>Background BPM + key analysis over unanalyzed library tracks.</summary>
public sealed class AnalysisWorker
{
    private readonly LibraryDatabase _database;
    private CancellationTokenSource? _cts;

    public event EventHandler<AnalysisResult>? TrackAnalyzed;
    public event EventHandler? Completed;
    public event EventHandler<int>? ProgressChanged;  // pending count remaining

    public bool IsRunning { get; private set; }

    public AnalysisWorker(LibraryDatabase database)
    {
        _database = database;
    }

    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        _cts = new CancellationTokenSource();
        _ = RunAsync(_cts.Token);
    }

    public void Cancel() => _cts?.Cancel();

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var pending = _database.GetAllTracks()
                .Where(track => track.Bpm is null && File.Exists(track.SourcePath))
                .Select(track => track.SourcePath)
                .ToList();

            var remaining = pending.Count;
            ProgressChanged?.Invoke(this, remaining);

            foreach (var path in pending)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                await AnalyzeTrackAsync(path, ct);
                remaining--;
                ProgressChanged?.Invoke(this, remaining);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsRunning = false;
            if (!ct.IsCancellationRequested)
            {
                Completed?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>Analyzes one file and persists bpm/key; also used for on-demand analysis.</summary>
    public async Task<AnalysisResult?> AnalyzeTrackAsync(string path, CancellationToken ct = default)
    {
        try
        {
            var (bpm, firstBeat) = await BpmAnalyzer.AnalyzeAsync(path, ct);
            var key = await Task.Run(() => KeyAnalyzer.AnalyzeFile(path), ct);

            ct.ThrowIfCancellationRequested();

            _database.UpdateAnalysis(path, bpm, string.IsNullOrWhiteSpace(key) ? null : key);

            var result = new AnalysisResult(path, bpm, key, firstBeat);
            TrackAnalyzed?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;  // unreadable file; skip
        }
    }
}
