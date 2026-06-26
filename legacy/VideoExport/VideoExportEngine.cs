using System.Diagnostics;
using System.Buffers;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NAudio.Vorbis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Spectralis;

internal static class VideoExportEngine
{
    private static readonly EmbeddedVisualizerRenderer embeddedRenderer = new();

    public static async Task ExportAsync(
        AudioTrackInfo track,
        VisualizerMode mode,
        VisualizerTheme theme,
        bool showPeaks,
        VideoExportOptions options,
        IProgress<float> progress,
        CancellationToken ct)
    {
        ValidateExportInputs(track, options);

        var selectedVisualizer = options.Visualizer
            ?? (track.EmbeddedVisualizer is not null
                ? VideoExportVisualizerOption.Embedded(track.EmbeddedVisualizer)
                : VideoExportVisualizerOption.BuiltIn(mode));

        using var outputScope = ExportOutputScope.Create(options.OutputPath);
        var finalOutputPath = options.OutputPath;
        options.OutputPath = outputScope.WorkingPath;

        try
        {
            if (!options.AutoCycleVisualizers && selectedVisualizer.HtmlContext is not null)
            {
                // HTML visualizer: WebView2 requires the UI thread - do not wrap in Task.Run.
                await RunHtmlExportAsync(track, selectedVisualizer.HtmlContext, theme, showPeaks, options, progress, ct);
                outputScope.Commit();
                progress?.Report(1f);
                return;
            }

            if (!options.AutoCycleVisualizers && selectedVisualizer.VideoContext is not null)
            {
                await RunVideoExportAsync(track, selectedVisualizer.VideoContext, theme, showPeaks, options, progress, ct);
                outputScope.Commit();
                progress?.Report(1f);
                return;
            }

            Image? albumArt = LoadAlbumArt(track);
            try
            {
                await Task.Run(async () => await RunExportAsync(track, mode, theme, showPeaks, albumArt, options, progress, ct), ct);
            }
            finally
            {
                albumArt?.Dispose();
            }

            outputScope.Commit();
            progress?.Report(1f);
        }
        finally
        {
            options.OutputPath = finalOutputPath;
        }
    }

    private static async Task RunExportAsync(
        AudioTrackInfo track,
        VisualizerMode mode,
        VisualizerTheme theme,
        bool showPeaks,
        Image? albumArt,
        VideoExportOptions options,
        IProgress<float> progress,
        CancellationToken ct)
    {
        var ffmpegPath = FindFfmpegPath();

        // Decode audio only for FFT/waveform analysis; FFmpeg reads the original file for encoding.
        using var audioStream = OpenAudioStream(track.FilePath!, options.MidiInstrument);
        var sampleRate = audioStream.WaveFormat.SampleRate;
        var ch = audioStream.WaveFormat.Channels;
        var timing = VideoExportTiming.Create(track, audioStream, options.FrameRate);
        var totalSeconds = (float)timing.DurationSeconds;

        ISampleProvider rawProvider = audioStream as ISampleProvider ?? audioStream.ToSampleProvider();
        var visProvider = new VisualizerSampleProvider(rawProvider);
        var audioBuffer = CreateAnalysisBuffer(sampleRate, ch, options.FrameRate);

        using var renderSequence = VideoRenderSequence.Create(track, mode, options);
        var state = new VideoFrameState();

        var w = options.Width;
        var h = options.Height;
        var fps = options.FrameRate;
        var crf = Math.Clamp(12 + (int)((100 - options.Quality) * 28.0 / 99.0), 12, 40);
        var bounds = new Rectangle(0, 0, w, h);

        // Start FFmpeg: reads raw BGRA frames from stdin, audio from the original file.
        using var ffmpegAudioInput = FfmpegAudioInput.Create(track, options.MidiInstrument, ct);
        var ffmpegArgs = BuildFfmpegArgs(ffmpegAudioInput.FilePath, options.OutputPath, w, h, fps, crf, timing.DurationSeconds);
        using var ffmpeg = StartFfmpeg(ffmpegPath, ffmpegArgs);
        using var killOnCancel = ct.Register(() => { try { if (!ffmpeg.HasExited) ffmpeg.Kill(); } catch { } });
        var stderrTask = ffmpeg.StandardError.ReadToEndAsync();
        var stdin = ffmpeg.StandardInput.BaseStream;

        // Double-buffer: render frame N while frame N-1 is being piped to FFmpeg.
        var bitmaps = new[]
        {
            new Bitmap(w, h, PixelFormat.Format32bppArgb),
            new Bitmap(w, h, PixelFormat.Format32bppArgb),
        };
        // Pre-allocate pixel buffers once (avoids per-frame GC pressure at 4K).
        var frameBuffers = new[] { new byte[w * h * 4], new byte[w * h * 4] };
        using var sceneFont = new Font("Segoe UI", h / 96f, GraphicsUnit.Point);

        Task? pendingWrite = null;

        try
        {
            for (var frame = 0; frame < timing.TotalFrames; frame++)
            {
                ct.ThrowIfCancellationRequested();

                var elapsed = (float)timing.GetFrameTimeSeconds(frame);
                ConsumeAnalysisSamples(visProvider, audioBuffer, timing.GetSampleFramesForFrame(frame), ch);

                var renderEntry = renderSequence.GetEntry(elapsed);
                state.Advance(visProvider.GetFrame(), renderEntry.Mode, showPeaks, 1f / fps);

                // Render current frame.
                var currentBitmap = bitmaps[frame & 1];
                var scene = BuildScene(state, renderEntry.Label, theme, albumArt, showPeaks, elapsed, sceneFont);
                using (var g = Graphics.FromImage(currentBitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    RenderFrame(g, bounds, scene, renderEntry.Definition, renderEntry.Session);
                    DrawOverlays(g, bounds, track, albumArt, elapsed, totalSeconds, theme, options);
                }

                // Await the previous stdin write before reusing that buffer slot.
                if (pendingWrite != null)
                    await pendingWrite;

                // Extract pixels and pipe to FFmpeg on a thread pool thread while we render the next frame.
                var buf = frameBuffers[frame & 1];
                var bmpToExtract = currentBitmap;
                pendingWrite = Task.Run(() =>
                {
                    ExtractBgraToBuffer(bmpToExtract, bounds, buf);
                    stdin.Write(buf, 0, buf.Length);
                }, ct);

                if (frame % 15 == 0)
                    progress?.Report(Math.Min(0.99f, (float)(frame + 1) / timing.TotalFrames));
            }

            if (pendingWrite != null)
                await pendingWrite;
        }
        finally
        {
            bitmaps[0].Dispose();
            bitmaps[1].Dispose();
        }

        // Signal end of video stream; FFmpeg will finish encoding and mux the audio.
        stdin.Close();
        await ffmpeg.WaitForExitAsync(ct);

        if (ffmpeg.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new Exception(
                $"FFmpeg exited with code {ffmpeg.ExitCode}. " +
                $"Ensure FFmpeg has libx264 support.\n{stderr.TrimEnd()}");
        }

        progress?.Report(0.995f);
    }

    private const string FfmpegExecutableName = "ffmpeg.exe";
    private const string FfmpegBundledPayloadName = "ffmpeg.bin";

    private static string FindFfmpegPath()
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var candidate = Path.Combine(appDir, FfmpegExecutableName);
            if (File.Exists(candidate))
                return candidate;

            var bundledPayload = Path.Combine(appDir, FfmpegBundledPayloadName);
            if (File.Exists(bundledPayload))
            {
                var prepared = PrepareFfmpegExecutable(bundledPayload);
                if (prepared is not null)
                    return prepared;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
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
            "FFmpeg not found. Place ffmpeg.exe in the application directory, " +
            "or install FFmpeg and add it to your system PATH.");
    }

    private static string? PrepareFfmpegExecutable(string payloadPath)
    {
        try
        {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis",
                "tools");
            Directory.CreateDirectory(cacheDir);

            var executablePath = Path.Combine(cacheDir, FfmpegExecutableName);
            if (!File.Exists(executablePath) ||
                File.GetLastWriteTimeUtc(executablePath) != File.GetLastWriteTimeUtc(payloadPath) ||
                new FileInfo(executablePath).Length != new FileInfo(payloadPath).Length)
            {
                File.Copy(payloadPath, executablePath, overwrite: true);
                File.SetLastWriteTimeUtc(executablePath, File.GetLastWriteTimeUtc(payloadPath));
            }

            return executablePath;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> BuildFfmpegArgs(
        string audioPath,
        string outputPath,
        int w,
        int h,
        int fps,
        int crf,
        double durationSeconds) =>
        [
            "-y",
            "-f", "rawvideo",
            "-pix_fmt", "bgra",
            "-s", $"{w}x{h}",
            "-r", fps.ToString(CultureInfo.InvariantCulture),
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
            "-t", FormatFfmpegSeconds(durationSeconds),
            "-movflags", "+faststart",
            outputPath
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
            }
        };

        foreach (var arg in args)
            p.StartInfo.ArgumentList.Add(arg);

        p.Start();
        return p;
    }

    private static void ValidateExportInputs(AudioTrackInfo track, VideoExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(track.FilePath) || !File.Exists(track.FilePath))
            throw new FileNotFoundException("The source audio file is no longer available.", track.FilePath);

        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("Choose an output file path first.", nameof(options));

        var sourcePath = Path.GetFullPath(track.FilePath);
        var outputPath = Path.GetFullPath(options.OutputPath);
        if (string.Equals(sourcePath, outputPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose an output file that is different from the source audio file.");
    }

    private static string FormatFfmpegSeconds(double seconds) =>
        Math.Max(0.001d, seconds).ToString("0.######", CultureInfo.InvariantCulture);

    private static float[] CreateAnalysisBuffer(int sampleRate, int channels, int frameRate)
    {
        var maxSampleFramesPerFrame = (int)Math.Ceiling(sampleRate / (double)Math.Max(1, frameRate)) + 2;
        return new float[Math.Max(maxSampleFramesPerFrame * Math.Max(1, channels), 4096)];
    }

    private static void ConsumeAnalysisSamples(
        VisualizerSampleProvider provider,
        float[] buffer,
        int sampleFrames,
        int channels)
    {
        var remainingSamples = Math.Max(0, sampleFrames) * Math.Max(1, channels);
        while (remainingSamples > 0)
        {
            var requested = Math.Min(buffer.Length, remainingSamples);
            var samplesRead = provider.Read(buffer, 0, requested);
            if (samplesRead > 0)
            {
                remainingSamples -= samplesRead;
                continue;
            }

            Array.Clear(buffer, 0, requested);
            provider.FeedExternalSamples(buffer, 0, requested, channels);
            remainingSamples -= requested;
        }
    }

    private sealed class ExportOutputScope : IDisposable
    {
        private readonly string finalPath;
        private bool committed;

        private ExportOutputScope(string finalPath, string workingPath)
        {
            this.finalPath = finalPath;
            WorkingPath = workingPath;
        }

        public string WorkingPath { get; }

        public static ExportOutputScope Create(string finalPath)
        {
            var normalizedFinalPath = Path.GetFullPath(finalPath);
            var directory = Path.GetDirectoryName(normalizedFinalPath)
                ?? throw new InvalidOperationException("Choose a valid output folder.");
            Directory.CreateDirectory(directory);

            var fileName = Path.GetFileNameWithoutExtension(normalizedFinalPath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "export";

            var workingPath = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():N}.tmp.mp4");
            return new ExportOutputScope(normalizedFinalPath, workingPath);
        }

        public void Commit()
        {
            if (committed)
                return;

            File.Move(WorkingPath, finalPath, overwrite: true);
            committed = true;
        }

        public void Dispose()
        {
            if (committed)
                return;

            try
            {
                if (File.Exists(WorkingPath))
                    File.Delete(WorkingPath);
            }
            catch { }
        }
    }

    private static void ExtractBgraToBuffer(Bitmap bitmap, Rectangle bounds, byte[] buffer)
    {
        var data = bitmap.LockBits(bounds, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var rowBytes = bounds.Width * 4;
            if (data.Stride == rowBytes)
            {
                Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            }
            else
            {
                for (var y = 0; y < bounds.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, buffer, y * rowBytes, rowBytes);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void RenderFrame(
        Graphics g,
        Rectangle bounds,
        VisualizerScene scene,
        VisualizerDefinition definition,
        EmbeddedVisualizerSession? embeddedSession)
    {
        if (embeddedSession is { IsFaulted: false })
        {
            var instructions = embeddedSession.Render(scene);
            if (!embeddedSession.IsFaulted)
            {
                embeddedRenderer.Draw(g, bounds, scene, instructions);
                return;
            }
        }
        definition.Renderer.Draw(g, bounds, scene);
    }

    private static void DrawOverlays(
        Graphics g,
        Rectangle bounds,
        AudioTrackInfo track,
        Image? albumArt,
        float elapsed,
        float total,
        VisualizerTheme theme,
        VideoExportOptions options)
    {
        var scale = bounds.Height / 1080f;
        var margin = (int)(20 * scale);
        var barH = Math.Max(3, (int)(4 * scale));
        var barY = bounds.Bottom - margin - barH;

        if (options.ShowPlaybackBar)
            DrawPlaybackBar(g, bounds, elapsed, total, theme, margin, barH, barY, scale);

        var overlayBottom = options.ShowPlaybackBar ? barY - (int)(10 * scale) : bounds.Bottom - margin;

        if (options.ShowAlbumArt && albumArt != null)
            overlayBottom = DrawAlbumArt(g, bounds, albumArt, overlayBottom, margin, scale);

        if (options.ShowTrackInfo && !string.IsNullOrWhiteSpace(track.DisplayName))
            DrawTrackInfo(g, track, overlayBottom, margin, scale);
    }

    private static void DrawPlaybackBar(
        Graphics g,
        Rectangle bounds,
        float elapsed,
        float total,
        VisualizerTheme theme,
        int margin,
        int barH,
        int barY,
        float scale)
    {
        var barX = margin;
        var barW = bounds.Width - margin * 2;
        var bgRect = new Rectangle(barX, barY, barW, barH);

        using var bgBrush = new SolidBrush(Color.FromArgb(60, 0, 0, 0));
        g.FillRectangle(bgBrush, bgRect);

        var progress = total > 0 ? Math.Clamp(elapsed / total, 0f, 1f) : 0f;
        var fillW = (int)(barW * progress);
        if (fillW > 1)
        {
            var fillRect = new Rectangle(barX, barY, fillW, barH);
            using var fillBrush = new LinearGradientBrush(
                new Rectangle(barX, barY, barW, barH),
                theme.BarStartColor,
                theme.BarEndColor,
                LinearGradientMode.Horizontal);
            g.FillRectangle(fillBrush, fillRect);
        }

        // Timestamps
        var timeFontSize = Math.Max(7f, 11f * scale);
        using var timeFont = new Font("Segoe UI", timeFontSize, GraphicsUnit.Pixel);
        using var timeBrush = new SolidBrush(Color.FromArgb(190, 220, 220, 220));
        var leftTime = FormatTime(elapsed);
        var rightTime = FormatTime(total);
        var rightSize = g.MeasureString(rightTime, timeFont);
        var textY = barY - (int)(rightSize.Height + 4 * scale);
        g.DrawString(leftTime, timeFont, timeBrush, barX, textY);
        g.DrawString(rightTime, timeFont, timeBrush, barX + barW - rightSize.Width, textY);
    }

    private static int DrawAlbumArt(
        Graphics g,
        Rectangle bounds,
        Image albumArt,
        int overlayBottom,
        int margin,
        float scale)
    {
        var artSize = (int)(Math.Min(bounds.Width, bounds.Height) * 0.14f);
        var artX = bounds.Right - margin - artSize;
        var artY = overlayBottom - artSize;
        var artRect = new Rectangle(artX, artY, artSize, artSize);

        // Shadow
        using var shadowBrush = new SolidBrush(Color.FromArgb(70, 0, 0, 0));
        var shadowRect = new Rectangle(artX + 3, artY + 3, artSize, artSize);
        using var shadowPath = RoundedPath(shadowRect, (int)(8 * scale));
        g.FillPath(shadowBrush, shadowPath);

        // Clip to rounded rect
        using var artPath = RoundedPath(artRect, (int)(8 * scale));
        g.SetClip(artPath);
        g.DrawImage(albumArt, artRect);
        g.ResetClip();

        return artY - (int)(8 * scale);
    }

    private static void DrawTrackInfo(
        Graphics g,
        AudioTrackInfo track,
        int overlayBottom,
        int margin,
        float scale)
    {
        var titleFontSize = Math.Max(10f, 22f * scale);
        var artistFontSize = Math.Max(8f, 15f * scale);
        var pad = (int)(12 * scale);
        var gap = (int)(4 * scale);
        var radius = (int)(10 * scale);

        using var titleFont = new Font("Segoe UI Semibold", titleFontSize, GraphicsUnit.Pixel);
        using var artistFont = new Font("Segoe UI", artistFontSize, GraphicsUnit.Pixel);

        var title = track.DisplayName;
        var artist = track.Artist ?? "";
        var titleSize = g.MeasureString(title, titleFont);
        var artistSize = !string.IsNullOrWhiteSpace(artist) ? g.MeasureString(artist, artistFont) : SizeF.Empty;

        var panelW = (int)Math.Max(titleSize.Width, artistSize.Width) + pad * 2;
        var textH = (int)(titleSize.Height + (artistSize.Height > 0 ? gap + artistSize.Height : 0));
        var panelH = textH + pad * 2;
        var panelY = overlayBottom - panelH;

        var panelRect = new Rectangle(margin, panelY, panelW, panelH);

        using var bgBrush = new SolidBrush(Color.FromArgb(165, 0, 0, 0));
        using var panelPath = RoundedPath(panelRect, radius);
        g.FillPath(bgBrush, panelPath);

        using var titleBrush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        g.DrawString(title, titleFont, titleBrush, margin + pad, panelY + pad);

        if (artistSize.Height > 0)
        {
            using var artistBrush = new SolidBrush(Color.FromArgb(185, 200, 200, 200));
            g.DrawString(artist, artistFont, artistBrush, margin + pad, panelY + pad + (int)titleSize.Height + gap);
        }
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        var d = Math.Max(2, Math.Min(radius, Math.Min(r.Width, r.Height) / 2) * 2);
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static VisualizerScene BuildScene(
        VideoFrameState state,
        string label,
        VisualizerTheme theme,
        Image? albumArt,
        bool showPeaks,
        float playbackSeconds,
        Font font) =>
        new()
        {
            Font = font,
            ModeLabel = label,
            Theme = theme,
            SpectrumLevels = state.SpectrumLevels,
            PeakHoldLevels = state.PeakHoldLevels,
            WaveformPoints = state.WaveformPoints,
            PeakLevel = state.PeakLevel,
            RmsLevel = state.RmsLevel,
            PlaybackTimeSeconds = playbackSeconds,
            IsActive = true,
            ShowPeaks = showPeaks,
            AlbumArt = albumArt,
            DiskAngle = state.DiskAngle,
            AnimationPhase = state.AnimationPhase,
            MidiNotes = [],
            MidiInstrumentName = null,
        };

    private static Image? LoadAlbumArt(AudioTrackInfo track)
    {
        if (track.AlbumArtBytes is not { Length: > 0 } bytes)
            return null;
        try { return Image.FromStream(new MemoryStream(bytes)); }
        catch { return null; }
    }

    private static WaveStream OpenAudioStream(string path, MidiPlaybackInstrument midiInstrument)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        if (ext is ".mid" or ".midi" or ".kar")
            return new MidiPlaybackStream(path, MidiSoundFontLocator.ResolveDefaultSoundFontPath(), midiInstrument);

        // VorbisWaveReader handles OGG natively; AudioFileReader doesn't support it reliably
        if (ext is ".ogg" or ".oga")
            return new VorbisWaveReader(path);

        // AudioFileReader normalizes any format to IEEE float32 at the original sample rate,
        // which eliminates the ToSampleProvider() format-dispatch chain that can produce
        // clicking artifacts with non-standard bit depths (24-bit PCM, etc.).
        // It also implements ISampleProvider directly, so no extra wrapper is needed.
        try
        {
            return new AudioFileReader(path);
        }
        catch
        {
            // Fall back to format-specific readers if AudioFileReader can't open the file
            return ext switch
            {
                ".wav" => new WaveFileReader(path),
                ".mp3" => new Mp3FileReader(path),
                ".aif" or ".aifc" or ".aiff" => new AiffFileReader(path),
                _ => new MediaFoundationReader(path),
            };
        }
    }

    private sealed class FfmpegAudioInput : IDisposable
    {
        private readonly bool deleteOnDispose;

        private FfmpegAudioInput(string filePath, bool deleteOnDispose)
        {
            FilePath = filePath;
            this.deleteOnDispose = deleteOnDispose;
        }

        public string FilePath { get; }

        public static FfmpegAudioInput Create(
            AudioTrackInfo track,
            MidiPlaybackInstrument midiInstrument,
            CancellationToken cancellationToken)
        {
            if (!track.IsMidi)
                return new FfmpegAudioInput(track.FilePath, deleteOnDispose: false);

            var directory = Path.Combine(Path.GetTempPath(), "Spectralis", "VideoExport");
            Directory.CreateDirectory(directory);
            var tempPath = Path.Combine(directory, $"{Guid.NewGuid():N}.wav");

            try
            {
                using var stream = OpenAudioStream(track.FilePath, midiInstrument);
                using var writer = new WaveFileWriter(tempPath, stream.WaveFormat);
                var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(4096, stream.WaveFormat.AverageBytesPerSecond / 2));
                try
                {
                    int bytesRead;
                    while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writer.Write(buffer, 0, bytesRead);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }

                return new FfmpegAudioInput(tempPath, deleteOnDispose: true);
            }
            catch
            {
                try { File.Delete(tempPath); } catch { }
                throw;
            }
        }

        public void Dispose()
        {
            if (deleteOnDispose)
            {
                try { File.Delete(FilePath); } catch { }
            }
        }
    }

    private static string FormatTime(float seconds)
    {
        var ts = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";
    }

    private static async Task RunHtmlExportAsync(
        AudioTrackInfo track,
        EmbeddedHtmlContext htmlContext,
        VisualizerTheme theme,
        bool showPeaks,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        await RunWebViewExportAsync(
            track,
            PrepareHtmlForExport(htmlContext),
            theme,
            showPeaks,
            options,
            progress,
            syncEmbeddedVideo: false,
            ct);
    }

    private static async Task RunVideoExportAsync(
        AudioTrackInfo track,
        EmbeddedVideoContext videoContext,
        VisualizerTheme theme,
        bool showPeaks,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        var tempVideoPath = Path.Combine(
            Path.GetTempPath(),
            $"spectralis_export_video_{Guid.NewGuid():N}{GetVideoFileExtensionForExport(videoContext)}");

        try
        {
            await File.WriteAllBytesAsync(tempVideoPath, videoContext.VideoBytes, ct);
            await RunWebViewExportAsync(
                track,
                PrepareVideoHtmlForExport(videoContext, tempVideoPath),
                theme,
                showPeaks,
                options,
                progress,
                syncEmbeddedVideo: true,
                ct);
        }
        finally
        {
            try { File.Delete(tempVideoPath); } catch { }
        }
    }

    private static async Task RunWebViewExportAsync(
        AudioTrackInfo track,
        string html,
        VisualizerTheme theme,
        bool showPeaks,
        VideoExportOptions options,
        IProgress<float>? progress,
        bool syncEmbeddedVideo,
        CancellationToken ct)
    {
        var ffmpegPath = FindFfmpegPath();

        using var audioStream = OpenAudioStream(track.FilePath!, options.MidiInstrument);
        var sampleRate = audioStream.WaveFormat.SampleRate;
        var ch = audioStream.WaveFormat.Channels;
        var timing = VideoExportTiming.Create(track, audioStream, options.FrameRate);
        var totalSeconds = (float)timing.DurationSeconds;
        ISampleProvider rawProvider = audioStream as ISampleProvider ?? audioStream.ToSampleProvider();
        var visProvider = new VisualizerSampleProvider(rawProvider);
        var audioBuffer = CreateAnalysisBuffer(sampleRate, ch, options.FrameRate);
        var state = new VideoFrameState();
        using var albumArt = LoadAlbumArt(track);
        var bounds = new Rectangle(0, 0, options.Width, options.Height);

        var fps = options.FrameRate;
        var crf = Math.Clamp(12 + (int)((100 - options.Quality) * 28.0 / 99.0), 12, 40);

        using var ffmpegAudioInput = FfmpegAudioInput.Create(track, options.MidiInstrument, ct);
        var ffmpegArgs = BuildFfmpegArgsForHtml(ffmpegAudioInput.FilePath, options.OutputPath, fps, crf, timing.DurationSeconds);
        using var ffmpeg = StartFfmpeg(ffmpegPath, ffmpegArgs);
        using var killOnCancel = ct.Register(() => { try { if (!ffmpeg.HasExited) ffmpeg.Kill(); } catch { } });
        var stderrTask = ffmpeg.StandardError.ReadToEndAsync();
        var stdin = ffmpeg.StandardInput.BaseStream;

        var captureForm = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            Opacity = 0,
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-options.Width - 200, 0),
            ClientSize = new Size(options.Width, options.Height),
        };
        var webView = new WebView2 { Dock = DockStyle.Fill };
        captureForm.Controls.Add(webView);

        try
        {
            captureForm.Show();

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Spectralis", "WebView2Cache");
            Directory.CreateDirectory(userDataFolder);
            var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await webView.EnsureCoreWebView2Async(env);

            webView.CoreWebView2.Settings.IsScriptEnabled = true;
            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.DefaultBackgroundColor = System.Drawing.Color.Black;
            webView.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) &&
                    !e.Uri.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
                    e.Cancel = true;
            };

            var domTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            webView.CoreWebView2.DOMContentLoaded += (_, _) => domTcs.TrySetResult(true);

            var tempHtml = Path.Combine(Path.GetTempPath(), $"spectralis_export_{Guid.NewGuid():N}.html");
            try
            {
                File.WriteAllText(tempHtml, html, Encoding.UTF8);
                webView.CoreWebView2.Navigate(new Uri(tempHtml).AbsoluteUri);
                await domTcs.Task.WaitAsync(TimeSpan.FromSeconds(15), ct);
            }
            finally
            {
                try { File.Delete(tempHtml); } catch { }
            }

            // Let the page run its first animation frame before we start driving it.
            await Task.Delay(400, ct);
            if (syncEmbeddedVideo)
                await WaitForEmbeddedVideoReadyAsync(webView, ct);

            Task? pendingWrite = null;

            for (var frame = 0; frame < timing.TotalFrames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                var elapsed = (float)timing.GetFrameTimeSeconds(frame);
                ConsumeAnalysisSamples(visProvider, audioBuffer, timing.GetSampleFramesForFrame(frame), ch);
                state.Advance(visProvider.GetFrame(), VisualizerMode.MirrorSpectrum, showPeaks, 1f / fps);

                var frameScript = BuildWebViewFrameScript(state, elapsed, syncEmbeddedVideo);

                await webView.CoreWebView2.ExecuteScriptAsync(frameScript);

                using var pngStream = new MemoryStream();
                await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, pngStream);
                var pngBytes = EncodeWebViewFrame(pngStream, bounds, track, albumArt, elapsed, totalSeconds, theme, options);

                if (pendingWrite is not null)
                    await pendingWrite;

                pendingWrite = Task.Run(() => stdin.Write(pngBytes, 0, pngBytes.Length), ct);

                if (frame % 15 == 0)
                    progress?.Report(Math.Min(0.99f, (float)(frame + 1) / timing.TotalFrames));
            }

            if (pendingWrite is not null)
                await pendingWrite;
        }
        finally
        {
            captureForm.Close();
            captureForm.Dispose();
        }

        stdin.Close();
        await ffmpeg.WaitForExitAsync(ct);

        if (ffmpeg.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new Exception(
                $"FFmpeg exited with code {ffmpeg.ExitCode}.\n{stderr.TrimEnd()}");
        }

        progress?.Report(0.995f);
    }

    private static IReadOnlyList<string> BuildFfmpegArgsForHtml(
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
            "-t", FormatFfmpegSeconds(durationSeconds),
            "-movflags", "+faststart",
            outputPath
        ];

    private static async Task WaitForEmbeddedVideoReadyAsync(WebView2 webView, CancellationToken ct)
    {
        const string script = """
            (function(){
              var video = window.currentVideoElement || document.querySelector('video');
              if (!video) return Promise.resolve(false);
              video.pause();
              if (video.readyState >= 1) return Promise.resolve(true);
              return new Promise(function(resolve){
                var done = function(value){
                  video.removeEventListener('loadedmetadata', onLoaded);
                  video.removeEventListener('error', onError);
                  resolve(value);
                };
                var onLoaded = function(){ done(true); };
                var onError = function(){ done(false); };
                video.addEventListener('loadedmetadata', onLoaded, { once: true });
                video.addEventListener('error', onError, { once: true });
                setTimeout(function(){ done(video.readyState >= 1); }, 5000);
              });
            })()
            """;

        await webView.CoreWebView2.ExecuteScriptAsync(script).WaitAsync(TimeSpan.FromSeconds(6), ct);
    }

    private static string BuildWebViewFrameScript(VideoFrameState state, float elapsed, bool syncEmbeddedVideo)
    {
        var levelsJson = BuildLevelsJson(state.SpectrumLevels);
        var peak = Math.Clamp(state.PeakLevel, 0f, 1.25f).ToString("0.0000", CultureInfo.InvariantCulture);
        var rms = Math.Clamp(state.RmsLevel, 0f, 1.25f).ToString("0.0000", CultureInfo.InvariantCulture);
        var time = elapsed.ToString("0.0000", CultureInfo.InvariantCulture);
        var shouldSyncVideo = syncEmbeddedVideo ? "true" : "false";

        return
            "(function(){" +
            $"var f={{time:{time},levels:{levelsJson},peak:{peak},rms:{rms},active:true}};" +
            "document.documentElement.style.setProperty('--audio-peak',String(f.peak));" +
            "document.documentElement.style.setProperty('--audio-rms',String(f.rms));" +
            "document.documentElement.style.setProperty('--audio-time',String(f.time));" +
            "document.documentElement.classList.add('audio-active');" +
            "if(typeof window.onSpectralisFrame==='function')window.onSpectralisFrame(f);" +
            "if(typeof window.onAudioTime==='function')window.onAudioTime(f.time);" +
            "var finish=function(){return new Promise(function(r){requestAnimationFrame(function(){requestAnimationFrame(r);});});};" +
            $"if(!{shouldSyncVideo})return finish();" +
            "var video=window.currentVideoElement||document.querySelector('video');" +
            "if(!video)return finish();" +
            "try{video.pause();}catch(e){}" +
            "var target=f.time;" +
            "var duration=Number(video.duration);" +
            "if(Number.isFinite(duration)&&duration>0){" +
            "if(video.loop||window.__spectralisVideoLoop)target=target%duration;" +
            "else target=Math.max(0,Math.min(target,Math.max(0,duration-0.001)));" +
            "}" +
            "if(video.readyState>=2&&Math.abs((video.currentTime||0)-target)<0.003)return finish();" +
            "return new Promise(function(resolve){" +
            "var settled=false;" +
            "var done=function(){if(settled)return;settled=true;video.removeEventListener('seeked',done);requestAnimationFrame(resolve);};" +
            "video.addEventListener('seeked',done);" +
            "setTimeout(done,350);" +
            "try{video.currentTime=target;}catch(e){done();}" +
            "}).then(finish);" +
            "})()";
    }

    private static byte[] EncodeWebViewFrame(
        MemoryStream pngStream,
        Rectangle bounds,
        AudioTrackInfo track,
        Image? albumArt,
        float elapsed,
        float totalSeconds,
        VisualizerTheme theme,
        VideoExportOptions options)
    {
        if (!options.ShowPlaybackBar &&
            !options.ShowTrackInfo &&
            (!options.ShowAlbumArt || albumArt is null))
        {
            return pngStream.ToArray();
        }

        pngStream.Position = 0;
        using var captured = Image.FromStream(pngStream);
        using var composed = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(composed))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(captured, bounds);
            DrawOverlays(g, bounds, track, albumArt, elapsed, totalSeconds, theme, options);
        }

        using var output = new MemoryStream();
        composed.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    private static string PrepareVideoHtmlForExport(EmbeddedVideoContext context, string videoPath)
    {
        var width = context.Width is > 0 ? context.Width.Value : 1280;
        var height = context.Height is > 0 ? context.Height.Value : 720;
        var mimeType = GetVideoMimeTypeForExport(context);
        var sourceUri = System.Net.WebUtility.HtmlEncode(new Uri(videoPath).AbsoluteUri);
        var loop = context.Loop ? "true" : "false";

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <style>
                html, body {
                  width: 100%;
                  height: 100%;
                  margin: 0;
                  overflow: hidden;
                  background: #000;
                }
                body {
                  display: flex;
                  align-items: center;
                  justify-content: center;
                }
                video {
                  width: 100vw;
                  height: 100vh;
                  object-fit: contain;
                  background: #000;
                }
              </style>
            </head>
            <body>
              <video width="{{width}}" height="{{height}}" muted playsinline preload="auto">
                <source src="{{sourceUri}}" type="{{mimeType}}">
              </video>
              <script>
                window.currentVideoElement = document.querySelector('video');
                window.__spectralisVideoLoop = {{loop}};
                if (window.currentVideoElement) {
                  window.currentVideoElement.pause();
                  window.syncVideoPosition = function(seconds) {
                    const video = window.currentVideoElement;
                    if (!video) return;
                    let target = Math.max(0, Number(seconds) || 0);
                    if (Number.isFinite(video.duration) && video.duration > 0) {
                      target = (video.loop || window.__spectralisVideoLoop)
                        ? target % video.duration
                        : Math.min(target, Math.max(0, video.duration - 0.001));
                    }
                    video.currentTime = target;
                  };
                }
              </script>
            </body>
            </html>
            """;
    }

    private static string PrepareHtmlForExport(EmbeddedHtmlContext context)
    {
        var html = Encoding.UTF8.GetString(context.HtmlBytes);

        // Replace delta-asset:/delta-bin: with inline data URIs
        html = Regex.Replace(html, @"delta-(?:asset|bin):([A-Za-z0-9_.\-]+)", m =>
        {
            var id = m.Groups[1].Value;
            if (!context.BinaryAssets.TryGetValue(id, out var bytes))
                return m.Value;
            var mime = DetectMimeTypeForExport(bytes);
            return mime is null ? m.Value : $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        }, RegexOptions.IgnoreCase);

        // Replace delta-data-json: with the JSON-serialised text value
        html = Regex.Replace(html, "\"?delta-data-json:([A-Za-z0-9_.-]+)\"?", m =>
        {
            var id = m.Groups[1].Value;
            if (!context.TextAssets.TryGetValue(id, out var text))
                return "null";
            return JsonSerializer.Serialize(text);
        }, RegexOptions.IgnoreCase);

        return html;
    }

    private static string? DetectMimeTypeForExport(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "image/jpeg";
        if (bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return "image/png";
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
            bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50)
            return "image/webp";
        if (bytes.Length >= 6 && bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46)
            return "image/gif";
        if (bytes.Length >= 12 && bytes[4] == 0x66 && bytes[5] == 0x74 && bytes[6] == 0x79 && bytes[7] == 0x70)
            return "video/mp4";
        if (bytes.Length >= 4 && bytes[0] == 0x1A && bytes[1] == 0x45 && bytes[2] == 0xDF && bytes[3] == 0xA3)
            return "video/webm";
        if (bytes.Length >= 4 && bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53)
            return "video/ogg";
        if (bytes.Length >= 4 && bytes[0] == 0x77 && bytes[1] == 0x4F && bytes[2] == 0x46 && bytes[3] == 0x32)
            return "font/woff2";
        if (bytes.Length >= 4 && bytes[0] == 0x77 && bytes[1] == 0x4F && bytes[2] == 0x46 && bytes[3] == 0x46)
            return "font/woff";
        return null;
    }

    private static string GetVideoMimeTypeForExport(EmbeddedVideoContext context)
    {
        if (DetectMimeTypeForExport(context.VideoBytes) is { } detected &&
            detected.StartsWith("video/", StringComparison.OrdinalIgnoreCase))
        {
            return detected;
        }

        return context.Codec.ToLowerInvariant() switch
        {
            "vp8" or "vp9" => "video/webm",
            "theora" => "video/ogg",
            _ => "video/mp4"
        };
    }

    private static string GetVideoFileExtensionForExport(EmbeddedVideoContext context) =>
        GetVideoMimeTypeForExport(context) switch
        {
            "video/webm" => ".webm",
            "video/ogg" => ".ogv",
            _ => ".mp4"
        };

    private static string BuildLevelsJson(float[] spectrum)
    {
        var sampled = SampleSpectrumForHtml(spectrum, 32);
        var sb = new StringBuilder("[");
        for (var i = 0; i < sampled.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.AppendFormat(CultureInfo.InvariantCulture, "{0:F4}", sampled[i]);
        }
        sb.Append(']');
        return sb.ToString();
    }

    private static float[] SampleSpectrumForHtml(float[] spectrum, int count)
    {
        var result = new float[count];
        if (spectrum.Length == 0) return result;
        for (var i = 0; i < count; i++)
        {
            var start = i * spectrum.Length / count;
            var end = Math.Max(start + 1, (i + 1) * spectrum.Length / count);
            var max = 0f;
            for (var j = start; j < end && j < spectrum.Length; j++)
                max = Math.Max(max, Math.Clamp(spectrum[j], 0, 1.25f));
            result[i] = max;
        }
        return result;
    }

    private sealed class VideoExportTiming
    {
        private long consumedSampleFrames;

        private VideoExportTiming(double durationSeconds, int frameRate, int sampleRate)
        {
            DurationSeconds = Math.Max(1d / Math.Max(1, frameRate), durationSeconds);
            FrameRate = Math.Max(1, frameRate);
            SampleRate = Math.Max(1, sampleRate);
            TotalFrames = Math.Max(1, (int)Math.Ceiling(DurationSeconds * FrameRate));
        }

        public double DurationSeconds { get; }

        public int FrameRate { get; }

        public int SampleRate { get; }

        public int TotalFrames { get; }

        public static VideoExportTiming Create(AudioTrackInfo track, WaveStream audioStream, int frameRate)
        {
            var durationSeconds = ResolveExportDurationSeconds(track, audioStream.TotalTime);
            return new VideoExportTiming(durationSeconds, frameRate, audioStream.WaveFormat.SampleRate);
        }

        public double GetFrameTimeSeconds(int frameIndex) =>
            Math.Min(DurationSeconds, Math.Max(0, frameIndex) / (double)FrameRate);

        public int GetSampleFramesForFrame(int frameIndex)
        {
            var targetSampleFrames = (long)Math.Round(
                ((Math.Max(0, frameIndex) + 1d) / FrameRate) * SampleRate,
                MidpointRounding.AwayFromZero);
            var sampleFrames = Math.Max(0, targetSampleFrames - consumedSampleFrames);
            consumedSampleFrames = targetSampleFrames;
            return sampleFrames > int.MaxValue ? int.MaxValue : (int)sampleFrames;
        }

        private static double ResolveExportDurationSeconds(AudioTrackInfo track, TimeSpan decoderDuration)
        {
            var decoderSeconds = IsUsableDuration(decoderDuration) ? decoderDuration.TotalSeconds : 0d;
            var trackSeconds = IsUsableDuration(track.Duration) ? track.Duration.TotalSeconds : 0d;

            if (trackSeconds > 0d)
            {
                var authoredTrack =
                    string.Equals(track.FormatName, "Spectralis Capsule", StringComparison.OrdinalIgnoreCase) ||
                    track.EmbeddedVisualizer is not null ||
                    track.EmbeddedHtml is not null ||
                    track.EmbeddedVideo is not null;

                if (authoredTrack || decoderSeconds <= 0d)
                    return trackSeconds;
            }

            if (decoderSeconds > 0d)
                return decoderSeconds;

            if (trackSeconds > 0d)
                return trackSeconds;

            throw new InvalidOperationException("Could not determine the source audio duration.");
        }

        private static bool IsUsableDuration(TimeSpan duration) =>
            duration > TimeSpan.Zero &&
            !double.IsNaN(duration.TotalSeconds) &&
            !double.IsInfinity(duration.TotalSeconds);
    }

    private sealed class VideoRenderSequence : IDisposable
    {
        private readonly VideoRenderEntry[] entries;
        private readonly int cycleSeconds;

        private VideoRenderSequence(VideoRenderEntry[] entries, int cycleSeconds)
        {
            this.entries = entries.Length > 0
                ? entries
                : [VideoRenderEntry.BuiltIn(VisualizerMode.MirrorSpectrum)];
            this.cycleSeconds = Math.Max(1, cycleSeconds);
        }

        public static VideoRenderSequence Create(AudioTrackInfo track, VisualizerMode fallbackMode, VideoExportOptions options)
        {
            var selected = options.Visualizer
                ?? (track.EmbeddedVisualizer is not null
                    ? VideoExportVisualizerOption.Embedded(track.EmbeddedVisualizer)
                    : VideoExportVisualizerOption.BuiltIn(fallbackMode));

            IEnumerable<VideoExportVisualizerOption> source = options.AutoCycleVisualizers
                ? options.CycleVisualizers.Where(static option => option.CanRenderInFrameExporter)
                : new[] { selected };

            var entries = source
                .Where(static option => option.CanRenderInFrameExporter)
                .Select(VideoRenderEntry.Create)
                .ToArray();

            if (entries.Length == 0)
                entries = [VideoRenderEntry.BuiltIn(fallbackMode)];

            return new VideoRenderSequence(entries, options.VisualizerCycleSeconds);
        }

        public VideoRenderEntry GetEntry(float elapsedSeconds)
        {
            if (entries.Length == 1)
                return entries[0];

            var index = (int)(Math.Max(0, elapsedSeconds) / cycleSeconds) % entries.Length;
            return entries[index];
        }

        public void Dispose()
        {
            foreach (var entry in entries)
                entry.Dispose();
        }
    }

    private sealed class VideoRenderEntry : IDisposable
    {
        private VideoRenderEntry(
            VisualizerMode mode,
            VisualizerDefinition definition,
            string label,
            EmbeddedVisualizerSession? session)
        {
            Mode = mode;
            Definition = definition;
            Label = label;
            Session = session;
        }

        public VisualizerMode Mode { get; }

        public VisualizerDefinition Definition { get; }

        public string Label { get; }

        public EmbeddedVisualizerSession? Session { get; }

        public static VideoRenderEntry Create(VideoExportVisualizerOption option)
        {
            var definition = VisualizerCatalog.GetDefinition(option.Mode);
            var session = EmbeddedVisualizerSession.TryCreate(option.VisualizerContext);
            var label = session is { IsFaulted: false }
                ? session.DisplayLabel
                : definition.Label;

            return new VideoRenderEntry(option.Mode, definition, label, session);
        }

        public static VideoRenderEntry BuiltIn(VisualizerMode mode)
        {
            var definition = VisualizerCatalog.GetDefinition(mode);
            return new VideoRenderEntry(mode, definition, definition.Label, session: null);
        }

        public void Dispose() => Session?.Dispose();
    }

    // Mutable state carried between frames, simulating SpectrumVisualizerControl.UpdateFrame
    private sealed class VideoFrameState
    {
        public float[] SpectrumLevels { get; } = new float[64];
        public float[] PeakHoldLevels { get; } = new float[64];
        public float[] WaveformPoints { get; } = new float[256];
        public float PeakLevel { get; private set; }
        public float RmsLevel { get; private set; }
        public float DiskAngle { get; private set; }
        public float AnimationPhase { get; private set; }

        public void Advance(VisualizerFrame frame, VisualizerMode mode, bool showPeaks, float deltaSeconds)
        {
            var frameScale = Math.Clamp(deltaSeconds * 30f, 0.1f, 4f);
            var spectrumDecay = MathF.Pow(0.80f, frameScale);
            var waveformKeep = MathF.Pow(0.35f, frameScale);
            var peakDecay = MathF.Pow(0.90f, frameScale);
            var rmsDecay = MathF.Pow(0.92f, frameScale);

            for (var i = 0; i < SpectrumLevels.Length; i++)
            {
                var incoming = i < frame.Spectrum.Length ? Math.Clamp(frame.Spectrum[i], 0, 1.25f) : 0;
                SpectrumLevels[i] = Math.Max(incoming, SpectrumLevels[i] * spectrumDecay);
                PeakHoldLevels[i] = showPeaks ? Math.Max(SpectrumLevels[i], PeakHoldLevels[i] - (0.03f * frameScale)) : 0;
            }

            for (var i = 0; i < WaveformPoints.Length; i++)
            {
                var incoming = i < frame.Waveform.Length ? frame.Waveform[i] : 0;
                WaveformPoints[i] = (WaveformPoints[i] * waveformKeep) + (incoming * (1f - waveformKeep));
            }

            PeakLevel = Math.Max(frame.PeakLevel, PeakLevel * peakDecay);
            RmsLevel = Math.Max(frame.RmsLevel, RmsLevel * rmsDecay);

            if (mode == VisualizerMode.SpinningDisk)
                DiskAngle = (DiskAngle + (0.38f * frameScale)) % 360f;

            var phaseStep = mode switch
            {
                VisualizerMode.RadialSpectrum => 0.85f,
                VisualizerMode.Graph3D => 1.05f,
                VisualizerMode.DancingColors => 1.65f,
                VisualizerMode.Sphere3D => 0.90f,
                _ => 0f,
            };

            if (phaseStep > 0)
                AnimationPhase = (AnimationPhase + ((phaseStep + (frame.RmsLevel * 2.4f)) * frameScale)) % 360f;
        }
    }
}
