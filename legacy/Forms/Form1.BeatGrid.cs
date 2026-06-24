namespace Spectralis;

public partial class Form1
{
    private AnalysisWorker? analysisWorker;
    private BeatGrid?       currentBeatGrid;
    private MetronomeForm?  metronomeForm;

    private void InitializeBeatGrid()
    {
        // ── Library browser context menu hook ────────────────────────────────
        if (libraryBrowser is not null)
            libraryBrowser.AnalyzeBpmRequested += LibraryBrowser_AnalyzeBpmRequested;

        // ── File menu items ──────────────────────────────────────────────────
        var mniAnalyzeAll = new ToolStripMenuItem
        {
            Name = "mniAnalyzeAllBpm",
            Text = "Analyze Library BPM + Key...",
        };
        mniAnalyzeAll.Click += (_, _) => StartLibraryAnalysis();

        var mniMetronome = new ToolStripMenuItem
        {
            Name = "mniMetronome",
            Text = "Metronome...",
        };
        mniMetronome.Click += (_, _) => OpenMetronome();

        libraryToolStripMenuItem.DropDownItems.Add(mniAnalyzeAll);
        toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
        toolsToolStripMenuItem.DropDownItems.Add(mniMetronome);

        // ── Auto-analyze on startup ──────────────────────────────────────────
        if (appSettings.AutoAnalyzeBpm && musicLibrary.Tracks.Count > 0)
            StartLibraryAnalysis();
    }

    // ── Analysis ──────────────────────────────────────────────────────────────

    private void StartLibraryAnalysis()
    {
        if (analysisWorker?.IsRunning == true) return;
        if (libraryStore is null) return;

        analysisWorker = new AnalysisWorker(musicLibrary, libraryStore);
        analysisWorker.TrackAnalyzed  += AnalysisWorker_TrackAnalyzed;
        analysisWorker.ProgressChanged += AnalysisWorker_ProgressChanged;
        analysisWorker.Completed      += (_, _) => BeginInvoke(() =>
        {
            toolStripStatusLabel.Text = "";
        });
        analysisWorker.Start();
    }

    private void AnalysisWorker_TrackAnalyzed(object? sender, AnalysisResult result)
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            // If this is the currently loaded track, update beat grid
            if (string.Equals(engine.CurrentTrack?.FilePath, result.Path,
                               StringComparison.OrdinalIgnoreCase))
            {
                currentBeatGrid = new BeatGrid(result.Bpm, result.FirstBeatOffset, 4, result.Key);
            }
        });
    }

    private void AnalysisWorker_ProgressChanged(object? sender, int remaining)
    {
        if (!IsHandleCreated || IsDisposed) return;
        BeginInvoke(() =>
        {
            toolStripStatusLabel.Text = remaining > 0
                ? $"Analyzing: {remaining} left"
                : "";
        });
    }

    private void LibraryBrowser_AnalyzeBpmRequested(object? sender, string[] paths)
    {
        if (libraryStore is null || paths.Length == 0) return;

        _ = Task.Run(async () =>
        {
            foreach (var path in paths)
            {
                try
                {
                    var (bpm, firstBeat) = await BpmAnalyzer.AnalyzeAsync(path);
                    var key = await Task.Run(() =>
                    {
                        try
                        {
                            using var reader = new NAudio.Wave.AudioFileReader(path);
                            var sr = reader.WaveFormat.SampleRate;
                            var ch = reader.WaveFormat.Channels;
                            var len = Math.Min(30 * sr * ch, (int)(reader.Length / sizeof(float)));
                            var raw = new float[len];
                            var read = reader.Read(raw, 0, len);
                            var mono = new float[read / ch];
                            for (var i = 0; i < mono.Length; i++)
                            {
                                var sum = 0f;
                                for (var c = 0; c < ch; c++) sum += raw[i * ch + c];
                                mono[i] = sum / ch;
                            }
                            return KeyAnalyzer.Analyze(mono, sr);
                        }
                        catch { return ""; }
                    });

                    var existing = musicLibrary.Find(path);
                    if (existing is not null)
                    {
                        var updated = existing with { Bpm = bpm, Key = string.IsNullOrWhiteSpace(key) ? null : key };
                        musicLibrary.UpsertAnalysis(updated);
                        libraryStore.UpdateAnalysis(path, bpm, key);
                    }
                }
                catch { }
            }
        });
    }

    // ── Metronome ─────────────────────────────────────────────────────────────

    private void OpenMetronome()
    {
        if (metronomeForm is { IsDisposed: false })
        {
            metronomeForm.BringToFront();
            return;
        }

        var initialBpm = currentBeatGrid?.Bpm ?? 120f;
        metronomeForm = new MetronomeForm(themePalette, initialBpm);
        metronomeForm.Show(this);
    }

    // ── Partial hook: called after local file loads ───────────────────────────

    partial void OnBeatGridTrackLoaded(string path)
    {
        var track = musicLibrary.Find(path);
        if (track?.Bpm is { } bpm)
        {
            currentBeatGrid = new BeatGrid(bpm, TimeSpan.Zero, 4, track.Key ?? "");
        }
        else
        {
            currentBeatGrid = null;
            // Trigger on-demand analysis for this track
            if (libraryStore is not null)
                _ = AnalyzeCurrentTrackAsync(path);
        }
    }

    private async Task AnalyzeCurrentTrackAsync(string path)
    {
        try
        {
            var (bpm, firstBeat) = await BpmAnalyzer.AnalyzeAsync(path);
            var existing = musicLibrary.Find(path);
            if (existing is not null)
            {
                var updated = existing with { Bpm = bpm };
                musicLibrary.UpsertAnalysis(updated);
                libraryStore?.UpdateAnalysis(path, bpm, null);
            }

            if (!IsHandleCreated || IsDisposed) return;
            BeginInvoke(() =>
            {
                if (string.Equals(engine.CurrentTrack?.FilePath, path, StringComparison.OrdinalIgnoreCase))
                    currentBeatGrid = new BeatGrid(bpm, firstBeat, 4, "");
            });
        }
        catch { }
    }
}
