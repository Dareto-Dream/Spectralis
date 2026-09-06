using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NAudio.Vorbis;
using NAudio.Wave;
using NLayer.NAudioSupport;
using Spectralis.App.Controls;
using Spectralis.Core.Visualizers;
using Spectralis.Core.Visualizers.Scripting;

namespace Spectralis.App.VideoExport;

public static partial class VideoExportEngine
{
    public static async Task ExportAsync(
        VideoExportRequest request,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(request.AudioFilePath))
            throw new FileNotFoundException("Audio file not found.", request.AudioFilePath);
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("No output path specified.", nameof(options));
        if (options.Visualizers.Count == 0)
            throw new ArgumentException("No visualizer selected.", nameof(options));

        var primary = options.PrimaryVisualizer;
        if (primary.IsWebView)
        {
#if WINDOWS
            if (OperatingSystem.IsWindows())
            {
                await RunWebViewExportAsync(request, options, progress, ct);
                return;
            }
#endif
            throw new PlatformNotSupportedException(
                "HTML and embedded-video visualizers can only be exported on Windows.");
        }

        await RunCanvasExportAsync(request, options, progress, ct);
    }

    // ── Built-in / scripted visualizer path (cross-platform, C#-drawn) ──────────

    private static async Task RunCanvasExportAsync(
        VideoExportRequest request,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        var ffmpegPath = FindFfmpegPath();
        var w = options.Width;
        var h = options.Height;
        var fps = options.FrameRate;
        var bounds = new VizRect(0, 0, w, h);

        using var audioStream = OpenAudioStream(request.AudioFilePath);
        var sampleRate = audioStream.WaveFormat.SampleRate;
        var channels = audioStream.WaveFormat.Channels;
        var durationSeconds = audioStream.TotalTime.TotalSeconds;
        if (durationSeconds <= 0)
            throw new InvalidOperationException(
                "Could not determine audio duration. The file may be unsupported or corrupt.");

        ISampleProvider rawProvider = audioStream is ISampleProvider sp ? sp : audioStream.ToSampleProvider();
        var visProvider = new VisualizerSampleProvider(rawProvider);
        var analysisBuffer = CreateAnalysisBuffer(sampleRate, channels, fps);

        var totalFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        long consumedSampleFrames = 0;

        var hasAlbumArt = request.AlbumArtBytes is { Length: > 0 };
        var sequence = VisualizerSequence.Build(options, hasAlbumArt);

        var sceneState = new VisualizerSceneState
        {
            Palette = VisualizerPalette.Default,
            AlbumArt = AvaloniaVizImage.FromBytes(request.AlbumArtBytes),
        };

        Bitmap? overlayCover = null;
        if (options.ShowAlbumArt && hasAlbumArt)
        {
            try { overlayCover = new Bitmap(new MemoryStream(request.AlbumArtBytes!)); }
            catch { overlayCover = null; }
        }

        using var outputScope = ExportOutputScope.Create(options.OutputPath);
        var ffmpegArgs = BuildFfmpegArgs(request.AudioFilePath, outputScope.WorkingPath, fps, options.Crf, durationSeconds);
        using var ffmpeg = StartFfmpeg(ffmpegPath, ffmpegArgs);
        using var killOnCancel = ct.Register(() =>
        {
            try { if (!ffmpeg.HasExited) ffmpeg.Kill(); }
            catch { }
        });
        var stderrTask = ffmpeg.StandardError.ReadToEndAsync();
        var stdin = ffmpeg.StandardInput.BaseStream;

        var rtb = await Dispatcher.UIThread.InvokeAsync(
            () => new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96)));

        try
        {
            await Task.Run(async () =>
            {
                for (var frame = 0; frame < totalFrames; frame++)
                {
                    ct.ThrowIfCancellationRequested();

                    var elapsed = (float)Math.Min(durationSeconds, Math.Max(0, frame) / (double)fps);

                    var targetSamples = (long)Math.Round(((frame + 1.0) / fps) * sampleRate, MidpointRounding.AwayFromZero);
                    var samplesToConsume = (int)Math.Min(Math.Max(0, targetSamples - consumedSampleFrames), int.MaxValue);
                    consumedSampleFrames = targetSamples;
                    ConsumeAnalysisSamples(visProvider, analysisBuffer, samplesToConsume, channels);

                    var entry = sequence.Resolve(elapsed);
                    var vizFrame = visProvider.GetFrame();
                    sceneState.UpdateFrame(vizFrame, true, elapsed, entry.Mode);
                    var scene = sceneState.CreateScene(entry.Label);

                    var overlayModel = BuildOverlayModel(request, options, overlayCover, elapsed, durationSeconds);

                    var pngBytes = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        using (var dc = rtb.CreateDrawingContext())
                        {
                            var canvas = new AvaloniaVizCanvas(dc);
                            canvas.FillRect(bounds, new VizColor(255, 0, 0, 0));
                            entry.Renderer.Draw(canvas, bounds, scene);
                            VideoOverlayRenderer.Draw(dc, w, h, overlayModel);
                        }
                        using var ms = new MemoryStream();
                        rtb.Save(ms);
                        return ms.ToArray();
                    });

                    await stdin.WriteAsync(pngBytes, 0, pngBytes.Length, ct);

                    if (frame % 15 == 0)
                        progress?.Report(Math.Min(0.99f, (float)(frame + 1) / totalFrames));
                }
            }, CancellationToken.None); // inner loop handles ct; don't cancel Task.Run itself
        }
        finally
        {
            rtb.Dispose();
            overlayCover?.Dispose();
        }

        stdin.Close();
        await ffmpeg.WaitForExitAsync(ct);

        if (ffmpeg.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(
                $"FFmpeg exited with code {ffmpeg.ExitCode}. Ensure FFmpeg has libx264 support.\n{stderr.TrimEnd()}");
        }

        outputScope.Commit();
        progress?.Report(1f);
    }

    internal static VideoOverlayModel BuildOverlayModel(
        VideoExportRequest request,
        VideoExportOptions options,
        Bitmap? cover,
        double elapsedSeconds,
        double totalSeconds) =>
        new()
        {
            ShowTitle = options.ShowTitle,
            ShowArtist = options.ShowArtist,
            ShowAlbum = options.ShowAlbum,
            ShowAlbumArt = options.ShowAlbumArt && cover is not null,
            ShowProgressBar = options.ShowProgressBar,
            Title = request.Title,
            Artist = request.Artist,
            Album = request.Album,
            Cover = cover,
            ElapsedSeconds = elapsedSeconds,
            TotalSeconds = totalSeconds,
        };

    // ── Auto-cycle sequence ───────────────────────────────────────────────────

    private sealed class VisualizerSequence
    {
        private readonly VisualizerEntry[] _entries;
        private readonly int _cycleSeconds;

        private VisualizerSequence(VisualizerEntry[] entries, int cycleSeconds)
        {
            _entries = entries.Length > 0 ? entries : [VisualizerEntry.BuiltIn(VisualizerMode.MirrorSpectrum, false)];
            _cycleSeconds = Math.Max(1, cycleSeconds);
        }

        public static VisualizerSequence Build(VideoExportOptions options, bool hasAlbumArt)
        {
            IEnumerable<VideoExportVisualizerSelection> source = options.AutoCycle
                ? options.Visualizers.Where(v => v.CanCycle)
                : [options.PrimaryVisualizer];

            var entries = source
                .Where(v => v.CanCycle)
                .Select(v => v.Script is { } script
                    ? VisualizerEntry.Scripted(script)
                    : VisualizerEntry.BuiltIn(v.Mode, hasAlbumArt))
                .ToArray();

            return new VisualizerSequence(entries, options.CycleSeconds);
        }

        public VisualizerEntry Resolve(float elapsedSeconds)
        {
            if (_entries.Length == 1)
                return _entries[0];

            var index = (int)(Math.Max(0, elapsedSeconds) / _cycleSeconds) % _entries.Length;
            return _entries[index];
        }
    }

    private sealed class VisualizerEntry
    {
        private VisualizerEntry(IVisualizerRenderer renderer, string label, VisualizerMode mode)
        {
            Renderer = renderer;
            Label = label;
            Mode = mode;
        }

        public IVisualizerRenderer Renderer { get; }
        public string Label { get; }
        public VisualizerMode Mode { get; }

        public static VisualizerEntry BuiltIn(VisualizerMode mode, bool hasAlbumArt)
        {
            var resolved = VisualizerCatalog.GetPreferredMode(mode, hasAlbumArt);
            var definition = VisualizerCatalog.GetDefinition(resolved);
            return new VisualizerEntry(definition.Renderer, definition.Label, resolved);
        }

        public static VisualizerEntry Scripted(ScriptedVisualizerDefinition def) =>
            new(new ScriptVisualizerRenderer(def), $"Script: {def.Name}", VisualizerMode.MirrorSpectrum);
    }

    // ── Atomic output ─────────────────────────────────────────────────────────

    private sealed class ExportOutputScope : IDisposable
    {
        private readonly string _finalPath;
        private bool _committed;

        private ExportOutputScope(string finalPath, string workingPath)
        {
            _finalPath = finalPath;
            WorkingPath = workingPath;
        }

        public string WorkingPath { get; }

        public static ExportOutputScope Create(string finalPath)
        {
            var normalized = Path.GetFullPath(finalPath);
            var directory = Path.GetDirectoryName(normalized)
                ?? throw new InvalidOperationException("Choose a valid output folder.");
            Directory.CreateDirectory(directory);

            var stem = Path.GetFileNameWithoutExtension(normalized);
            if (string.IsNullOrWhiteSpace(stem))
                stem = "export";

            return new ExportOutputScope(normalized, Path.Combine(directory, $".{stem}.{Guid.NewGuid():N}.tmp.mp4"));
        }

        public void Commit()
        {
            if (_committed)
                return;
            File.Move(WorkingPath, _finalPath, overwrite: true);
            _committed = true;
        }

        public void Dispose()
        {
            if (_committed)
                return;
            try { if (File.Exists(WorkingPath)) File.Delete(WorkingPath); }
            catch { }
        }
    }

    // ── Shared audio / ffmpeg plumbing ────────────────────────────────────────

    private static WaveStream OpenAudioStream(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".ogg" or ".oga")
            return new VorbisWaveReader(path);

        try
        {
            return new AudioFileReader(path);
        }
        catch
        {
            return ext switch
            {
                ".wav" => new WaveFileReader(path),
                ".mp3" => new Mp3FileReaderBase(path, wf => new Mp3FrameDecompressor(wf)),
                _ => new MediaFoundationReader(path),
            };
        }
    }

    private static float[] CreateAnalysisBuffer(int sampleRate, int channels, int fps)
    {
        var maxSamplesPerFrame = (int)Math.Ceiling(sampleRate / (double)Math.Max(1, fps)) + 2;
        return new float[Math.Max(maxSamplesPerFrame * Math.Max(1, channels), 4096)];
    }

    private static void ConsumeAnalysisSamples(
        VisualizerSampleProvider provider,
        float[] buffer,
        int sampleFrames,
        int channels)
    {
        var remaining = Math.Max(0, sampleFrames) * Math.Max(1, channels);
        while (remaining > 0)
        {
            var requested = Math.Min(buffer.Length, remaining);
            var read = provider.Read(buffer, 0, requested);
            if (read > 0)
            {
                remaining -= read;
                continue;
            }
            Array.Clear(buffer, 0, requested);
            provider.FeedExternalSamples(buffer, 0, requested, channels);
            remaining -= requested;
        }
    }

    private static IReadOnlyList<string> BuildFfmpegArgs(
        string audioPath,
        string outputPath,
        int fps,
        int crf,
        double durationSeconds) =>
    [
        "-y",
        "-f", "image2pipe",
        "-vcodec", "png",
        "-framerate", fps.ToString(CultureInfo.InvariantCulture),
        "-i", "pipe:0",
        "-i", audioPath,
        "-map", "0:v:0",
        "-map", "1:a:0",
        "-c:v", "libx264",
        "-preset", "fast",
        "-crf", crf.ToString(CultureInfo.InvariantCulture),
        "-pix_fmt", "yuv420p",
        "-af", "apad",
        "-c:a", "aac",
        "-b:a", "192k",
        "-t", durationSeconds.ToString("0.######", CultureInfo.InvariantCulture),
        "-movflags", "+faststart",
        outputPath,
    ];

    private static Process StartFfmpeg(string ffmpegPath, IEnumerable<string> args)
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };
        foreach (var arg in args)
            p.StartInfo.ArgumentList.Add(arg);
        p.Start();
        return p;
    }

    // The bundled/PATH binary has no ".exe" suffix outside Windows — searching
    // for "ffmpeg.exe" on Linux/macOS never matches a real ffmpeg install.
    private static string FfmpegExecutableName => OperatingSystem.IsWindows() ? "ffmpeg.exe" : "ffmpeg";

    private static string FindFfmpegPath()
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var bundled = Path.Combine(appDir, FfmpegExecutableName);
            if (File.Exists(bundled))
            {
                EnsureExecutable(bundled);
                return bundled;
            }
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, FfmpegExecutableName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        throw new FileNotFoundException(
            $"FFmpeg not found. Place {FfmpegExecutableName} in the Spectralis application folder, " +
            "or install FFmpeg and add it to your system PATH.");
    }

    /// <summary>Re-asserts the executable bit on the bundled binary (git tracks
    /// it explicitly since this repo is cross-published from Windows) so a
    /// stray build step or plain copy deploy can't silently break launch.</summary>
    private static void EnsureExecutable(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            const UnixFileMode exec =
                UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            var mode = File.GetUnixFileMode(path);
            if ((mode & exec) != exec)
            {
                File.SetUnixFileMode(path, mode | exec);
            }
        }
        catch
        {
            // Best-effort; if this fails, Process.Start surfaces a clear error.
        }
    }
}
