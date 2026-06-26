using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.VideoExport;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Views;

public partial class VideoExportWindow : Window
{
    private readonly string _audioFilePath;
    private readonly byte[]? _albumArtBytes;
    private readonly Action<bool>? _setIsExporting;
    private CancellationTokenSource? _cts;

    private static readonly (string Label, int Width, int Height)[] Resolutions =
    [
        ("1280 × 720  (HD)", 1280, 720),
        ("1920 × 1080  (Full HD)", 1920, 1080),
        ("854 × 480  (SD)", 854, 480),
    ];

    private static readonly int[] FrameRates = [30, 60, 24];

    private static readonly VisualizerMode[] ExportModes =
    [
        VisualizerMode.MirrorSpectrum,
        VisualizerMode.Spectrum,
        VisualizerMode.Waveform,
        VisualizerMode.RadialSpectrum,
        VisualizerMode.SpectrumWave,
        VisualizerMode.DancingColors,
        VisualizerMode.Sphere3D,
        VisualizerMode.Oscilloscope,
        VisualizerMode.VUMeter,
        VisualizerMode.SpinningDisk,
        VisualizerMode.AlbumCover,
        VisualizerMode.Graph3D,
    ];

    public VideoExportWindow(
        string audioFilePath,
        string? title,
        string? artist,
        byte[]? albumArtBytes,
        VisualizerMode currentMode,
        Action<bool>? setIsExporting = null)
    {
        _audioFilePath = audioFilePath;
        _albumArtBytes = albumArtBytes;
        _setIsExporting = setIsExporting;

        InitializeComponent();

        TrackLabel.Text = BuildTrackLabel(title, artist);

        ResolutionBox.ItemsSource = Resolutions.Select(r => r.Label).ToArray();
        ResolutionBox.SelectedIndex = 0;

        FpsBox.ItemsSource = FrameRates.Select(f => $"{f} fps").ToArray();
        FpsBox.SelectedIndex = 0;

        ModeBox.ItemsSource = ExportModes
            .Select(m => VisualizerCatalog.GetDefinition(m).Label)
            .ToArray();
        var modeIdx = Array.IndexOf(ExportModes, currentMode);
        ModeBox.SelectedIndex = modeIdx >= 0 ? modeIdx : 0;

        // Default output path beside the source file
        var defaultDir = System.IO.Path.GetDirectoryName(audioFilePath) ?? "";
        var stem = System.IO.Path.GetFileNameWithoutExtension(audioFilePath);
        OutputPathBox.Text = System.IO.Path.Combine(defaultDir, stem + ".mp4");
    }

    private static string BuildTrackLabel(string? title, string? artist)
    {
        if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(artist))
            return $"{artist} — {title}";
        return title ?? artist ?? "No track loaded";
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save video as…",
            SuggestedFileName = System.IO.Path.GetFileNameWithoutExtension(_audioFilePath) + ".mp4",
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

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var outputPath = OutputPathBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Choose an output file path first.", isError: true);
            return;
        }

        var res = Resolutions[Math.Max(0, ResolutionBox.SelectedIndex)];
        var fps = FrameRates[Math.Max(0, FpsBox.SelectedIndex)];
        var mode = ExportModes[Math.Max(0, ModeBox.SelectedIndex)];

        var options = new VideoExportOptions
        {
            Width = res.Width,
            Height = res.Height,
            FrameRate = fps,
            Mode = mode,
            OutputPath = outputPath,
        };

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
            await VideoExportEngine.ExportAsync(
                _audioFilePath, _albumArtBytes, options, progress, ct);

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

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
    }

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
