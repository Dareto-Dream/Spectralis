#if WINDOWS
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using NAudio.Wave;
using Spectralis.App.Controls;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.VideoExport;

public static partial class VideoExportEngine
{
    /// <summary>
    /// Exports an HTML or embedded-video visualizer by driving an offscreen WebView2 one
    /// frame at a time: push the audio frame → let the page render → capture the viewport →
    /// composite the overlay → pipe the PNG to FFmpeg. Windows only. Runs on the UI thread
    /// (WebView2 requires it) but awaits frequently so the export window stays responsive.
    /// </summary>
    [SupportedOSPlatform("windows")]
    private static async Task RunWebViewExportAsync(
        VideoExportRequest request,
        VideoExportOptions options,
        IProgress<float>? progress,
        CancellationToken ct)
    {
        var selection = options.PrimaryVisualizer;
        var ffmpegPath = FindFfmpegPath();
        var w = options.Width;
        var h = options.Height;
        var fps = options.FrameRate;

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
        var sceneState = new VisualizerSceneState { Palette = VisualizerPalette.Default };

        var totalFrames = Math.Max(1, (int)Math.Ceiling(durationSeconds * fps));
        long consumedSampleFrames = 0;

        var isVideo = selection.Video is not null;
        var tempDir = Path.Combine(Path.GetTempPath(), "spectralis", "video-export", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        Bitmap? overlayCover = null;
        if (options.ShowAlbumArt && request.AlbumArtBytes is { Length: > 0 } artBytes)
        {
            try { overlayCover = new Bitmap(new MemoryStream(artBytes)); }
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

        WebView2Host? host = null;
        Window? window = null;
        var compositor = new RenderTargetBitmap(new PixelSize(w, h), new Vector(96, 96));

        try
        {
            host = new WebView2Host
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                UserDataFolder = Path.Combine(Path.GetTempPath(), "spectralis-video-export-webview2"),
            };

            var navigated = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            host.NavigationCompleted += (_, _) => navigated.TrySetResult(true);
            host.NavigationFailed += (_, _) => navigated.TrySetResult(false);

            window = new Window
            {
                SystemDecorations = SystemDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Topmost = false,
                Background = Brushes.Black,
                // Just off the left edge — WebView2 keeps rendering an off-screen but
                // WS_VISIBLE window (matches the legacy WinForms capture host); a fully
                // negative/minimised window can get its compositor throttled.
                Position = new PixelPoint(-w - 200, 0),
                Width = w,
                Height = h,
                Content = host,
            };
            window.Show();

            // Match the WebView's client area to the exact output pixel size regardless of
            // the screen's DPI scaling, so CapturePreviewAsync returns a w×h PNG.
            var scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;
            window.Width = w / scaling;
            window.Height = h / scaling;
            await Task.Delay(50, ct);

            var meta = new EmbeddedTrackMeta(
                Title: request.Title,
                Artist: request.Artist,
                Album: request.Album,
                DurationSeconds: durationSeconds,
                ArtworkBytes: request.AlbumArtBytes,
                ArtworkMimeType: request.AlbumArtMimeType);

            if (isVideo)
            {
                var videoPath = Path.Combine(tempDir, "clip" + VideoExtension(selection.Video!.Codec));
                await File.WriteAllBytesAsync(videoPath, selection.Video!.VideoBytes, ct);
                var indexPath = Path.Combine(tempDir, "index.html");
                await File.WriteAllTextAsync(indexPath, BuildVideoHtml(Path.GetFileName(videoPath), selection.Video!), ct);
                host.MapVirtualHost("spectralis-export.local", tempDir);
                host.Navigate(new Uri("https://spectralis-export.local/index.html"));
            }
            else
            {
                var document = EmbeddedHtmlDocument.Build(selection.Html!, meta);
                host.NavigateToString(document);
            }

            var ok = await navigated.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            if (!ok)
                throw new InvalidOperationException("The visualizer page failed to load for export.");

            // Let the page run a few animation frames before we start stepping it.
            await Task.Delay(400, ct);

            for (var frame = 0; frame < totalFrames; frame++)
            {
                ct.ThrowIfCancellationRequested();

                var elapsed = (float)Math.Min(durationSeconds, Math.Max(0, frame) / (double)fps);

                var targetSamples = (long)Math.Round(((frame + 1.0) / fps) * sampleRate, MidpointRounding.AwayFromZero);
                var samplesToConsume = (int)Math.Min(Math.Max(0, targetSamples - consumedSampleFrames), int.MaxValue);
                consumedSampleFrames = targetSamples;
                ConsumeAnalysisSamples(visProvider, analysisBuffer, samplesToConsume, channels);

                sceneState.UpdateFrame(visProvider.GetFrame(), true, elapsed, VisualizerMode.MirrorSpectrum);
                var scene = sceneState.CreateScene("HTML");

                await host.ExecuteScriptAsync(BuildFrameScript(scene, elapsed, isVideo));
                // One real animation frame's worth of time for the page to paint.
                await Task.Delay(16, ct);

                var capturedPng = await host.CapturePngAsync();

                var overlayModel = BuildOverlayModel(request, options, overlayCover, elapsed, durationSeconds);
                var pngBytes = ComposeFrame(compositor, w, h, capturedPng, overlayModel);

                await stdin.WriteAsync(pngBytes, 0, pngBytes.Length, ct);

                if (frame % 15 == 0)
                    progress?.Report(Math.Min(0.99f, (float)(frame + 1) / totalFrames));
            }
        }
        finally
        {
            compositor.Dispose();
            overlayCover?.Dispose();
            if (window is not null)
            {
                window.Content = null;
                window.Close();
            }
            host?.Dispose();
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }

        stdin.Close();
        await ffmpeg.WaitForExitAsync(ct);

        if (ffmpeg.ExitCode != 0)
        {
            var stderr = await stderrTask;
            throw new InvalidOperationException(
                $"FFmpeg exited with code {ffmpeg.ExitCode}.\n{stderr.TrimEnd()}");
        }

        outputScope.Commit();
        progress?.Report(1f);
    }

    private static byte[] ComposeFrame(
        RenderTargetBitmap compositor, int w, int h, byte[] capturedPng, VideoOverlayModel overlay)
    {
        using var captured = new Bitmap(new MemoryStream(capturedPng));
        using (var dc = compositor.CreateDrawingContext())
        {
            dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, w, h));
            dc.DrawImage(captured, new Rect(captured.Size), new Rect(0, 0, w, h));
            VideoOverlayRenderer.Draw(dc, w, h, overlay);
        }
        using var ms = new MemoryStream();
        compositor.Save(ms);
        return ms.ToArray();
    }

    private static string BuildFrameScript(VisualizerScene scene, float elapsed, bool syncVideo)
    {
        var levels = SampleLevels(scene.SpectrumLevels, 64);
        var sb = new StringBuilder("[");
        for (var i = 0; i < levels.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(levels[i].ToString("0.####", CultureInfo.InvariantCulture));
        }
        sb.Append(']');

        var peak = Math.Clamp(scene.PeakLevel, 0f, 1.25f).ToString("0.####", CultureInfo.InvariantCulture);
        var rms = Math.Clamp(scene.RmsLevel, 0f, 1.25f).ToString("0.####", CultureInfo.InvariantCulture);
        var time = elapsed.ToString("0.####", CultureInfo.InvariantCulture);

        return
            "(function(){" +
            $"var f={{time:{time},levels:{sb},peak:{peak},rms:{rms},active:true}};" +
            "var d=document.documentElement.style;" +
            "d.setProperty('--audio-peak',String(f.peak));" +
            "d.setProperty('--audio-rms',String(f.rms));" +
            "d.setProperty('--audio-time',String(f.time));" +
            "document.documentElement.classList.add('audio-active');" +
            "if(typeof window.__spectralisReceiveFrame==='function')window.__spectralisReceiveFrame(f);" +
            "if(window.spectral){window.spectral._lastFrame=f;" +
            "if(typeof window.spectral.onPlaybackFrame==='function')window.spectral.onPlaybackFrame(f);}" +
            "if(typeof window.onSpectralisFrame==='function')window.onSpectralisFrame(f);" +
            "if(typeof window.onAudioTime==='function')window.onAudioTime(f.time);" +
            (syncVideo
                ? "var v=window.currentVideoElement||document.querySelector('video');" +
                  "if(v){try{v.pause();}catch(e){}" +
                  "var t=f.time,dur=Number(v.duration);" +
                  "if(Number.isFinite(dur)&&dur>0){t=(v.loop||window.__spectralisVideoLoop)?t%dur:Math.min(t,Math.max(0,dur-0.001));}" +
                  "try{v.currentTime=t;}catch(e){}}"
                : string.Empty) +
            "})()";
    }

    private static float[] SampleLevels(float[] spectrum, int count)
    {
        var result = new float[count];
        if (spectrum.Length == 0)
            return result;
        var ratio = (double)spectrum.Length / count;
        for (var i = 0; i < count; i++)
        {
            var src = (int)(i * ratio);
            result[i] = Math.Clamp(spectrum[Math.Min(src, spectrum.Length - 1)], 0f, 1.25f);
        }
        return result;
    }

    private static string VideoExtension(string codec) => codec.ToLowerInvariant() switch
    {
        "webm" or "vp8" or "vp9" or "av1" => ".webm",
        "ogg" or "theora" => ".ogv",
        _ => ".mp4",
    };

    private static string BuildVideoHtml(string fileName, Spectralis.Core.Embedded.EmbeddedVideoContext ctx)
    {
        var mime = VideoExtension(ctx.Codec) switch
        {
            ".webm" => "video/webm",
            ".ogv" => "video/ogg",
            _ => "video/mp4",
        };
        var loop = ctx.Loop ? "true" : "false";
        return $$"""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><style>
              html,body{width:100%;height:100%;margin:0;overflow:hidden;background:#000;display:flex;align-items:center;justify-content:center;}
              video{width:100vw;height:100vh;object-fit:contain;background:#000;}
            </style></head><body>
              <video muted playsinline preload="auto"><source src="{{fileName}}" type="{{mime}}"></video>
              <script>
                window.currentVideoElement=document.querySelector('video');
                window.__spectralisVideoLoop={{loop}};
                if(window.currentVideoElement){window.currentVideoElement.pause();}
              </script>
            </body></html>
            """;
    }
}
#endif
