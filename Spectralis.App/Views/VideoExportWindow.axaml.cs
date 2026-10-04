using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.VideoExport;
using Spectralis.App.ViewModels;
using Spectralis.Core.Embedded;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Installed;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.Views;

public partial class VideoExportWindow : Window
{
    private readonly VideoExportRequest _request;
    private readonly Action<bool>? _setIsExporting;
    private readonly List<VizRow> _rows = [];
    private CancellationTokenSource? _cts;
    private VideoExportPreset? _preset;
    private bool _applyingPreset;
    private readonly Action _queueChanged;

    private static readonly (string Label, int Width, int Height)[] Resolutions =
    [
        ("1920 × 1080  (Full HD)", 1920, 1080),
        ("1280 × 720  (HD)", 1280, 720),
        ("854 × 480  (SD)", 854, 480),
        ("2560 × 1440  (QHD)", 2560, 1440),
        ("3840 × 2160  (4K)", 3840, 2160),
        ("1080 × 1920  (Vertical 9:16)", 1080, 1920),
    ];

    private static readonly int[] FrameRates = [30, 60, 24];
    private const string CustomPresetLabel = "Custom";
    private static readonly int[] CycleSecondOptions = [4, 6, 8, 12, 16, 20, 30];

    /// <summary>Row shown in the visualizer list — a selectable export source plus its enabled state.</summary>
    private sealed class VizRow
    {
        public required string Label { get; init; }
        public required VideoExportVisualizerSelection Selection { get; init; }
        public bool IsEnabled { get; init; } = true;
        public override string ToString() => Label;
    }

    public VideoExportWindow(
        VideoExportRequest request,
        EmbeddedHtmlContext? trackHtml,
        EmbeddedVideoContext? trackVideo,
        VisualizerOption? currentSelection,
        Action<bool>? setIsExporting = null)
    {
        _request = request;
        _setIsExporting = setIsExporting;

        InitializeComponent();

        _queueChanged = () => Dispatcher.UIThread.Post(RefreshQueueList);
        VideoExportQueue.Shared.Changed += _queueChanged;
        Closed += (_, _) => VideoExportQueue.Shared.Changed -= _queueChanged;
        RefreshQueueList();

        TrackLabel.Text = BuildTrackLabel(request.Title, request.Artist);

        ResolutionBox.ItemsSource = Resolutions.Select(r => r.Label).ToArray();
        ResolutionBox.SelectedIndex = 0;

        FpsBox.ItemsSource = FrameRates.Select(f => $"{f} fps").ToArray();
        FpsBox.SelectedIndex = 0;

        PresetBox.ItemsSource = new[] { CustomPresetLabel }
            .Concat(VideoExportPreset.All.Select(p => p.Label)).ToArray();
        PresetBox.SelectedIndex = 0;

        CycleSecondsBox.ItemsSource = CycleSecondOptions.Select(s => $"{s}s").ToArray();
        CycleSecondsBox.SelectedIndex = Array.IndexOf(CycleSecondOptions, 12);

        QualitySlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                QualityValueLabel.Text = $"{(int)QualitySlider.Value}";
        };
        QualityValueLabel.Text = $"{(int)QualitySlider.Value}";

        ShowAlbumArtCheck.IsEnabled = request.AlbumArtBytes is { Length: > 0 };
        if (!ShowAlbumArtCheck.IsEnabled)
            ShowAlbumArtCheck.IsChecked = false;

        BuildVisualizerRows(trackHtml, trackVideo);
        VisualizerList.ItemsSource = _rows;
        PreselectVisualizer(currentSelection);

        var defaultDir = System.IO.Path.GetDirectoryName(request.AudioFilePath) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(request.AudioFilePath);
        OutputPathBox.Text = System.IO.Path.Combine(defaultDir, stem + ".mp4");
    }

    private void BuildVisualizerRows(EmbeddedHtmlContext? trackHtml, EmbeddedVideoContext? trackVideo)
    {
        var hasArt = _request.AlbumArtBytes is { Length: > 0 };
        var webViewSupported = OperatingSystem.IsWindows();

        foreach (var def in VisualizerCatalog.All)
        {
            if (def.RequiresMidi)
                continue;
            if (def.RequiresAlbumArt && !hasArt)
                continue;
            _rows.Add(new VizRow
            {
                Label = def.Label,
                Selection = VideoExportVisualizerSelection.BuiltIn(def.Mode),
            });
        }

        foreach (var script in ScriptedVisualizerStore.LoadAll())
        {
            _rows.Add(new VizRow
            {
                Label = $"Script: {script.Name}",
                Selection = VideoExportVisualizerSelection.Scripted(script),
            });
        }

        var store = new InstalledVisualizerStore();
        foreach (var installed in store.LoadAll())
        {
            var content = store.LoadContent(installed.Id);
            if (content is null)
                continue;
            var html = new EmbeddedHtmlContext(
                content.Id, content.HtmlBytes, content.BinaryAssets, content.TextAssets, content.Version);
            _rows.Add(new VizRow
            {
                Label = webViewSupported
                    ? $"Special: {installed.DisplayName}"
                    : $"Special: {installed.DisplayName}  (Windows only)",
                Selection = VideoExportVisualizerSelection.InstalledHtml(installed.DisplayName, html),
                IsEnabled = webViewSupported,
            });
        }

        if (trackHtml is not null)
        {
            _rows.Add(new VizRow
            {
                Label = webViewSupported
                    ? "This track's HTML visualizer"
                    : "This track's HTML visualizer  (Windows only)",
                Selection = VideoExportVisualizerSelection.TrackHtml(trackHtml),
                IsEnabled = webViewSupported,
            });
        }

        if (trackVideo is not null)
        {
            _rows.Add(new VizRow
            {
                Label = webViewSupported
                    ? "This track's embedded video"
                    : "This track's embedded video  (Windows only)",
                Selection = VideoExportVisualizerSelection.TrackVideo(trackVideo),
                IsEnabled = webViewSupported,
            });
        }
    }

    private void PreselectVisualizer(VisualizerOption? current)
    {
        var match = -1;
        if (current is not null)
        {
            match = _rows.FindIndex(r =>
                current.Script is { } s ? r.Selection.Script?.Id == s.Id
                : current.Installed is { } d ? r.Selection.Label.Contains(d.DisplayName, StringComparison.Ordinal)
                : r.Selection.Script is null && r.Selection.Html is null && r.Selection.Video is null
                  && r.Selection.Mode == current.Mode);
        }

        if (match < 0)
            match = _rows.FindIndex(r => r.IsEnabled);

        VisualizerList.SelectedIndex = Math.Max(0, match);
    }

    private static string BuildTrackLabel(string? title, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
            return $"{artist} — {title}";
        return title ?? artist ?? "No track loaded";
    }

    private void OnPresetChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset || PresetBox is null || ResolutionBox is null || FpsBox is null || QualitySlider is null)
            return;

        var index = PresetBox.SelectedIndex;
        _preset = index > 0 && index <= VideoExportPreset.All.Count ? VideoExportPreset.All[index - 1] : null;
        if (_preset is null)
            return;

        _applyingPreset = true;
        try
        {
            ResolutionBox.SelectedIndex = Math.Max(0,
                Array.FindIndex(Resolutions, r => r.Width == _preset.Width && r.Height == _preset.Height));
            FpsBox.SelectedIndex = Math.Max(0, Array.IndexOf(FrameRates, _preset.FrameRate));
            QualitySlider.Value = _preset.Quality;
        }
        finally { _applyingPreset = false; }
    }

    // Touching resolution/fps by hand means we're no longer on the preset (and its length cap).
    private void OnFormatEdited(object? sender, SelectionChangedEventArgs e)
    {
        if (_applyingPreset || PresetBox is null || _preset is null)
            return;

        _preset = null;
        _applyingPreset = true;
        try { PresetBox.SelectedIndex = 0; }
        finally { _applyingPreset = false; }
    }

    private void OnAutoCycleChanged(object? sender, RoutedEventArgs e)
    {
        if (CyclePanel is null || VisualizerList is null || AutoCycleCheck is null)
            return;

        var on = AutoCycleCheck.IsChecked == true;
        CyclePanel.IsVisible = on;
        VisualizerList.SelectionMode = on
            ? SelectionMode.Multiple | SelectionMode.Toggle
            : SelectionMode.Single;
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save video as…",
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_request.AudioFilePath) + ".mp4",
            FileTypeChoices =
            [
                new FilePickerFileType("MP4 video") { Patterns = ["*.mp4"] },
                FilePickerFileTypes.All,
            ],
            DefaultExtension = "mp4",
        });

        if (file?.TryGetLocalPath() is { } path)
            OutputPathBox.Text = path;
    }

    private IReadOnlyList<VideoExportVisualizerSelection> GatherSelections(bool autoCycle)
    {
        if (autoCycle)
        {
            var picked = VisualizerList.SelectedItems?
                .OfType<VizRow>()
                .Where(r => r.IsEnabled && r.Selection.CanCycle)
                .Select(r => r.Selection)
                .ToList() ?? [];
            return picked;
        }

        return VisualizerList.SelectedItem is VizRow row && row.IsEnabled
            ? [row.Selection]
            : [];
    }

    /// <summary>Reads the form into options, or reports the problem and returns null.</summary>
    private VideoExportOptions? BuildOptions()
    {
        var outputPath = OutputPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Choose an output file path first.", isError: true);
            return null;
        }

        var autoCycle = AutoCycleCheck.IsChecked == true;
        var selections = GatherSelections(autoCycle);
        if (selections.Count == 0)
        {
            SetStatus(autoCycle
                ? "Select at least one built-in or scripted visualizer to cycle."
                : "Select a visualizer.", isError: true);
            return null;
        }

        var res = Resolutions[Math.Max(0, ResolutionBox.SelectedIndex)];
        var fps = FrameRates[Math.Max(0, FpsBox.SelectedIndex)];
        var cycleSeconds = CycleSecondOptions[Math.Max(0, CycleSecondsBox.SelectedIndex)];

        var options = new VideoExportOptions
        {
            Width = res.Width,
            Height = res.Height,
            FrameRate = fps,
            Quality = (int)QualitySlider.Value,
            Visualizers = selections,
            AutoCycle = autoCycle && selections.Count > 1,
            CycleSeconds = cycleSeconds,
            ShowTitle = ShowTitleCheck.IsChecked == true,
            ShowArtist = ShowArtistCheck.IsChecked == true,
            ShowAlbum = ShowAlbumCheck.IsChecked == true,
            ShowAlbumArt = ShowAlbumArtCheck.IsChecked == true,
            ShowProgressBar = ShowProgressBarCheck.IsChecked == true,
            OutputPath = outputPath,
            MaxDurationSeconds = _preset?.MaxDurationSeconds,
        };

        return options;
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var options = BuildOptions();
        if (options is null)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        ExportButton.IsEnabled = false;
        CloseButton.Content = "Cancel";
        CloseButton.Click -= OnClose;
        CloseButton.Click += OnCancel;
        ExportProgress.IsVisible = true;
        ExportProgress.Value = 0;
        SetStatus("Rendering…", isError: false);
        _setIsExporting?.Invoke(true);

        var progress = new Progress<float>(p =>
        {
            ExportProgress.Value = Math.Round(p * 100, 1);
            StatusLabel.Text = $"Rendering… {p * 100:0}%";
        });

        try
        {
            await VideoExportEngine.ExportAsync(_request, options, progress, ct);

            ExportProgress.Value = 100;
            SetStatus("Export complete.", isError: false);
        }
        catch (OperationCanceledException)
        {
            ExportProgress.Value = 0;
            SetStatus("Export cancelled.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _setIsExporting?.Invoke(false);
            ExportButton.IsEnabled = true;
            CloseButton.Content = "Close";
            CloseButton.Click -= OnCancel;
            CloseButton.Click += OnClose;
        }
    }

    private void OnQueueAdd(object? sender, RoutedEventArgs e)
    {
        var options = BuildOptions();
        if (options is null)
            return;

        try
        {
            var job = VideoExportQueue.Shared.Enqueue(_request, options);
            SetStatus($"Queued → {System.IO.Path.GetFileName(job.Options.OutputPath)}", isError: false);
        }
        catch (InvalidOperationException ex)
        {
            SetStatus(ex.Message, isError: true);
        }
    }

    private async void OnQueueRun(object? sender, RoutedEventArgs e)
    {
        var queue = VideoExportQueue.Shared;
        if (queue.IsRunning || queue.Jobs.All(j => j.Status != VideoExportJobStatus.Pending))
        {
            SetStatus("Nothing pending in the queue.", isError: false);
            return;
        }

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        QueueRunButton.IsEnabled = false;
        QueueAddButton.IsEnabled = false;
        ExportButton.IsEnabled = false;
        CloseButton.Content = "Cancel";
        CloseButton.Click -= OnClose;
        CloseButton.Click += OnCancel;
        _setIsExporting?.Invoke(true);

        try
        {
            await queue.RunAsync(ct);
            var failed = queue.Jobs.Count(j => j.Status == VideoExportJobStatus.Failed);
            SetStatus(failed == 0 ? "Queue finished." : $"Queue finished, {failed} failed.", isError: failed > 0);
        }
        catch (Exception ex)
        {
            SetStatus(ex.Message, isError: true);
        }
        finally
        {
            _setIsExporting?.Invoke(false);
            QueueRunButton.IsEnabled = true;
            QueueAddButton.IsEnabled = true;
            ExportButton.IsEnabled = true;
            CloseButton.Content = "Close";
            CloseButton.Click -= OnCancel;
            CloseButton.Click += OnClose;
        }
    }

    private void OnQueueClear(object? sender, RoutedEventArgs e) => VideoExportQueue.Shared.ClearFinished();

    private void RefreshQueueList()
    {
        var jobs = VideoExportQueue.Shared.Jobs;
        QueueList.ItemsSource = jobs
            .Select(j => j.Status switch
            {
                VideoExportJobStatus.Running => $"▶ {j.Label}  {j.Progress * 100:0}%",
                VideoExportJobStatus.Done => $"✓ {j.Label}",
                VideoExportJobStatus.Failed => $"✗ {j.Label}  {j.Error}",
                VideoExportJobStatus.Cancelled => $"– {j.Label}  (cancelled)",
                _ => $"· {j.Label}",
            })
            .ToList();
        QueueHeader.Text = jobs.Count == 0 ? "Batch queue" : $"Batch queue ({jobs.Count})";
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _cts?.Cancel();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_cts is { IsCancellationRequested: false })
                _cts.Cancel();
            else
                Close();
            e.Handled = true;
        }
    }

    private void SetStatus(string text, bool isError)
    {
        StatusLabel.Text = text;
        StatusLabel.IsVisible = !string.IsNullOrEmpty(text);
        StatusLabel.Classes.Set("signal", isError);
    }
}
