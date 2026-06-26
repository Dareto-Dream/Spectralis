using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NAudio.Vorbis;
using NAudio.Wave;
using Spectralis.App.Controls;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.VideoExport;

public static class VideoExportEngine
{
    public static async Task ExportAsync(
        string audioFilePath,
        byte[]? albumArtBytes,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        if (!File.Exists(audioFilePath))
            throw new FileNotFoundException("Audio file not found.", audioFilePath);
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("No output path specified.", nameof(options));

        var ffmpegPath = FindFfmpegPath();
        var w = options.Width;
        var h = options.Height;
        var fps = options.FrameRate;
        var bounds = new VizRect(0, 0, w, h);

        using var audioStream = OpenAudioStream(audioFilePath);
        var sampleRate = audioStream.WaveFormat.SampleRate;
        var channels = audioStream.WaveFormat.Channels;
        var durationSeconds = audioStream.TotalTime.TotalSeconds;
        if (durationSeconds <= 0)
            throw new InvalidOperationException("Could not determine audio duration. The file may be unsupported or corrupt.");

        ISampleProvider rawProvider = audioStream is ISampleProvider sp ? sp : audioStream.ToSampleProvider();
        var visProvider = new VisualizerSampleProvider(rawProvider);
        var analysisBuffer = CreateAnalysisBuffer(sampleRate, channels, fps);

        var totalFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        long consumedSampleFrames = 0;

        var mode = options.Mode;
        var preferredMode = VisualizerCatalog.GetPreferredMode(mode, albumArtBytes?.Length > 0);
        var definition = VisualizerCatalog.GetDefinition(preferredMode);
        var sceneState = new VisualizerSceneState
        {
            Palette = VisualizerPalette.Default,
            AlbumArt = AvaloniaVizImage.FromBytes(albumArtBytes),
        };

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(options.OutputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var ffmpegArgs = BuildFfmpegArgs(audioFilePath, options.OutputPath, fps, durationSeconds);
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

                    var vizFrame = visProvider.GetFrame();
                    sceneState.UpdateFrame(vizFrame, true, elapsed, preferredMode);
                    var scene = sceneState.CreateScene(definition.Label);

                    var pngBytes = await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        using (var dc = rtb.CreateDrawingContext())
                        {
                            var canvas = new AvaloniaVizCanvas(dc);
                            canvas.FillRect(bounds, new VizColor(255, 0, 0, 0));
                            definition.Renderer.Draw(canvas, bounds, scene);
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
        }

        stdin.Close();
        await ffmpeg.WaitForExitAsync(ct);

        if (ffmpeg.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(
                $"FFmpeg exited with code {ffmpeg.ExitCode}. Ensure FFmpeg has libx264 support.\n{stderr.TrimEnd()}");
        }

        progress?.Report(1f);
    }

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
                ".mp3" => new Mp3FileReader(path),
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
        "-crf", "18",
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

    private static string FindFfmpegPath()
    {
        var appDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (appDir is not null)
        {
            var bundled = Path.Combine(appDir, "ffmpeg.exe");
            if (File.Exists(bundled))
                return bundled;
        }

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            try
            {
                var candidate = Path.Combine(dir, "ffmpeg.exe");
                if (File.Exists(candidate))
                    return candidate;
            }
            catch { }
        }

        throw new FileNotFoundException(
            "FFmpeg not found. Place ffmpeg.exe in the Spectralis application folder, " +
            "or install FFmpeg and add it to your system PATH.");
    }
}
