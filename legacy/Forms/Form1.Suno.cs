using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private static readonly HttpClient SunoMediaHttp = CreateSunoMediaHttp();

    private WebView2? sunoWebView;
    private Task? sunoPrewarmTask;
    private VisualizerSampleProvider? sunoVisualizer;
    private SpotifyLoopbackCapture? sunoLoopback;
    private AudioTrackInfo? sunoCurrentTrack;
    private SunoClipInfo? sunoCurrentClip;
    private string? sunoTempAudioPath;
    private bool sunoUsesNativePlayback;
    private bool sunoIsPlaying;
    private float sunoPositionSeconds;
    private float sunoDurationSeconds;
    private string? sunoStatusMessage;
    private CancellationTokenSource? sunoLoadCts;
    private int sunoLoadId;

    internal static readonly string SunoLogPath =
        AppLogPaths.For("suno.log");

    internal bool IsSunoActive => sunoCurrentTrack is not null;

    internal VisualizerFrame GetSunoVisualizerFrame() =>
        sunoUsesNativePlayback
            ? engine.GetVisualizerFrame()
            : sunoVisualizer?.GetFrame() ?? VisualizerFrame.Empty;

    private static HttpClient CreateSunoMediaHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return http;
    }

    private void LogSuno(string message)
    {
        try { AppLogPaths.AppendTimestamped(SunoLogPath, message); }
        catch { }
    }

    internal async Task LoadSunoUrlAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;

        LogSuno($"--- LoadSunoUrlAsync: {input}");

        if (IsSpotifyActive || spotifyService.IsLinked)
            ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
        StopRemoteAudioPlayback();
        StopLocalPlaybackForExternalUrl();
        if (IsSoundCloudActive)
            StopSoundCloudPlayback();
        if (IsYouTubeActive)
            StopYouTubePlayback();

        StopSunoPlayback();
        var loadId = ++sunoLoadId;
        var cts = sunoLoadCts = new CancellationTokenSource();
        SetSunoStatus("Suno loading...");
        UpdateUiState();

        SunoClipInfo clip;
        try
        {
            clip = await SunoClipResolver.ResolveAsync(input, cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSuno($"Resolve failed: {ex.Message}");
            if (loadId == sunoLoadId)
            {
                StopSunoPlayback();
                SetSunoStatus($"Suno: {ex.Message}");
            }
            return;
        }

        if (cts.IsCancellationRequested || loadId != sunoLoadId)
        {
            LogSuno($"Load {loadId} superseded before playback");
            return;
        }

        await ApplySunoTrackAsync(clip, cts.Token);
        if (cts.IsCancellationRequested || loadId != sunoLoadId)
            return;

        if (await TryStartNativeSunoPlaybackAsync(clip, loadId, cts.Token))
            return;

        if (cts.IsCancellationRequested || loadId != sunoLoadId)
            return;

        await StartSunoWebViewPlaybackAsync(clip, loadId);
    }

    private async Task<bool> TryStartNativeSunoPlaybackAsync(
        SunoClipInfo clip,
        int loadId,
        CancellationToken cancellationToken)
    {
        if (sunoCurrentTrack is null)
            return false;

        string? tempPath = null;
        try
        {
            SetSunoStatus("Suno buffering...");
            tempPath = await RemoteAudioCache.DownloadAsync(clip.AudioUrl, ".mp3", cancellationToken);
            if (cancellationToken.IsCancellationRequested || loadId != sunoLoadId)
            {
                RemoteAudioCache.TryDelete(tempPath);
                return true;
            }

            engine.Load(tempPath, sunoCurrentTrack);
            engine.Volume = trackBarVolume.Value / 100f;
            sunoTempAudioPath = tempPath;
            sunoUsesNativePlayback = true;
            sunoCurrentTrack = engine.CurrentTrack ?? sunoCurrentTrack;
            sunoPositionSeconds = 0;
            if (engine.GetLength() > 0)
                UpdateSunoDuration(engine.GetLength());

            sunoStatusMessage = null;
            engine.Play();
            SyncNativeExternalPlaybackState();
            UpdateAlbumArt(sunoCurrentTrack);
            UpdateUiState();
            LogSuno($"Native playback started from cached MP3: {tempPath}");
            return true;
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(tempPath);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSuno($"Native playback failed, falling back to WebView2: {ex.Message}");
            RemoteAudioCache.TryDelete(tempPath);
            sunoUsesNativePlayback = false;
            return false;
        }
    }

    private async Task StartSunoWebViewPlaybackAsync(SunoClipInfo clip, int loadId)
    {
        if (sunoWebView is null)
        {
            LogSuno("WebView2 not ready, awaiting prewarm");
            if (sunoPrewarmTask is not null)
            {
                try { await sunoPrewarmTask; }
                catch (Exception ex) { LogSuno($"Prewarm failed: {ex.Message}"); }
                sunoPrewarmTask = null;
            }

            if (sunoWebView is null)
                await InitializeSunoAsync(reportFailure: true);
        }

        if (sunoWebView?.CoreWebView2 is null)
        {
            LogSuno("WebView2 unavailable");
            SetSunoStatus("Suno unavailable");
            return;
        }

        var urlJson = JsonSerializer.Serialize(clip.AudioUrl);
        var volume = trackBarVolume.Value / 100d;
        sunoUsesNativePlayback = false;
        LogSuno($"Calling window.sunoLoad loadId={loadId} clip={clip.Id} title=\"{clip.Title}\"");
        try
        {
            var result = await sunoWebView.CoreWebView2.ExecuteScriptAsync(
                $"window.sunoLoad && window.sunoLoad({urlJson}, {loadId}, {volume:0.###})");
            LogSuno($"sunoLoad script result: {result}");
        }
        catch (Exception ex)
        {
            LogSuno($"sunoLoad script exception: {ex}");
            SetSunoStatus("Suno failed to load");
        }
    }

    private async Task InitializeSunoAsync(bool reportFailure = false)
    {
        if (sunoWebView is not null) return;

        LogSuno("InitializeSunoAsync start");
        try
        {
            sunoWebView = new WebView2
            {
                Enabled = false,
                Location = new System.Drawing.Point(-20, -20),
                Size = new System.Drawing.Size(2, 2),
                TabStop = false,
                Visible = true
            };
            Controls.Add(sunoWebView);
            sunoWebView.SendToBack();

            await sunoWebView.EnsureCoreWebView2Async(null);
            sunoWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            sunoWebView.CoreWebView2.WebMessageReceived += OnSunoWebMessage;
            sunoWebView.CoreWebView2.NavigateToString(BuildSunoHtml());
            LogSuno("InitializeSunoAsync complete");
        }
        catch (Exception ex)
        {
            LogSuno($"InitializeSunoAsync FAILED: {ex}");
            sunoWebView?.Dispose();
            sunoWebView = null;
            if (reportFailure)
                SetSunoStatus("Suno unavailable");
        }
    }

    private static string BuildSunoHtml() => """
        <!DOCTYPE html>
        <html><body style="margin:0;padding:0;">
        <audio id="suno-audio" crossorigin="anonymous" preload="auto"></audio>
        <script>
        const audio = document.getElementById('suno-audio');
        let loadGeneration = 0;
        const post = (loadId, p) => {
            p.loadId = loadId;
            window.chrome.webview.postMessage(JSON.stringify(p));
        };
        const log = message => post(loadGeneration, { type: 'log', message });
        const getDuration = () => Number.isFinite(audio.duration) ? audio.duration : 0;
        const getErrorMessage = () => {
            if (!audio.error) return 'Playback failed';
            switch (audio.error.code) {
                case MediaError.MEDIA_ERR_ABORTED: return 'Playback was aborted';
                case MediaError.MEDIA_ERR_NETWORK: return 'Network error while streaming';
                case MediaError.MEDIA_ERR_DECODE: return 'Audio decode failed';
                case MediaError.MEDIA_ERR_SRC_NOT_SUPPORTED: return 'Audio source is not supported';
                default: return 'Playback failed';
            }
        };
        const playCurrent = () => {
            const playPromise = audio.play();
            if (playPromise && playPromise.catch) {
                playPromise.catch(e => post(loadGeneration, {
                    type: 'play_failed',
                    message: e && e.message ? e.message : 'Playback was blocked'
                }));
            }
        };

        audio.addEventListener('loadedmetadata', () => post(loadGeneration, {
            type: 'loadedmetadata',
            duration: getDuration()
        }));
        audio.addEventListener('durationchange', () => post(loadGeneration, {
            type: 'loadedmetadata',
            duration: getDuration()
        }));
        audio.addEventListener('play', () => post(loadGeneration, { type: 'play' }));
        audio.addEventListener('playing', () => post(loadGeneration, { type: 'play' }));
        audio.addEventListener('pause', () => post(loadGeneration, {
            type: 'pause',
            position: audio.currentTime || 0,
            duration: getDuration()
        }));
        audio.addEventListener('ended', () => post(loadGeneration, {
            type: 'finish',
            position: audio.currentTime || 0,
            duration: getDuration()
        }));
        audio.addEventListener('timeupdate', () => post(loadGeneration, {
            type: 'progress',
            position: audio.currentTime || 0,
            duration: getDuration()
        }));
        audio.addEventListener('error', () => post(loadGeneration, {
            type: 'error',
            message: getErrorMessage()
        }));

        window.sunoLoad = (url, loadId, volume) => {
            loadGeneration = Number(loadId) || (loadGeneration + 1);
            audio.pause();
            audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
            audio.src = url;
            audio.currentTime = 0;
            audio.load();
            log('sunoLoad url=' + url + ' loadId=' + loadGeneration);
            playCurrent();
            return 'loading';
        };
        window.sunoPlay = () => playCurrent();
        window.sunoPause = () => audio.pause();
        window.sunoSeek = seconds => {
            if (Number.isFinite(Number(seconds))) audio.currentTime = Math.max(0, Number(seconds));
        };
        window.sunoSetVolume = volume => {
            audio.volume = Math.max(0, Math.min(1, Number(volume) || 0));
        };
        window.sunoStop = () => {
            audio.pause();
            audio.removeAttribute('src');
            audio.load();
        };
        </script>
        </body></html>
        """;

    private async void OnSunoWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            if (root.TryGetProperty("loadId", out var loadIdEl) &&
                loadIdEl.ValueKind == JsonValueKind.Number &&
                loadIdEl.TryGetInt32(out var messageLoadId) &&
                messageLoadId != sunoLoadId)
            {
                return;
            }

            var type = root.GetProperty("type").GetString();
            switch (type)
            {
                case "log":
                    var logMessage = root.TryGetProperty("message", out var logEl) ? logEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(logMessage))
                        LogSuno($"[JS] {logMessage}");
                    break;

                case "loadedmetadata":
                    var duration = root.TryGetProperty("duration", out var durEl) ? durEl.GetDouble() : 0;
                    UpdateSunoDuration(duration);
                    sunoStatusMessage = null;
                    UpdateUiState();
                    break;

                case "play":
                    sunoStatusMessage = null;
                    sunoIsPlaying = true;
                    if (sunoLoopback is null && sunoVisualizer is not null)
                    {
                        sunoLoopback = new SpotifyLoopbackCapture();
                        sunoLoopback.Start(sunoVisualizer);
                    }
                    UpdateUiState();
                    break;

                case "pause":
                    UpdateSunoProgress(root);
                    sunoIsPlaying = false;
                    StopSunoLoopback();
                    UpdateUiState();
                    break;

                case "finish":
                    UpdateSunoProgress(root);
                    OnSunoNaturalEnd();
                    break;

                case "progress":
                    UpdateSunoProgress(root);
                    break;

                case "play_failed":
                    var playMessage = root.TryGetProperty("message", out var playMsgEl) ? playMsgEl.GetString() : null;
                    LogSuno($"play_failed: {playMessage ?? "(null)"}");
                    sunoIsPlaying = false;
                    StopSunoLoopback();
                    SetSunoStatus("Suno ready");
                    break;

                case "error":
                    var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                    LogSuno($"error: {msg ?? "(null)"}");
                    StopSunoPlayback();
                    SetSunoStatus(string.IsNullOrWhiteSpace(msg) ? "Suno error" : $"Suno: {msg}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogSuno($"OnSunoWebMessage exception: {ex.Message}");
        }
    }

    private async Task ApplySunoTrackAsync(SunoClipInfo clip, CancellationToken cancellationToken)
    {
        sunoCurrentClip = clip;
        sunoDurationSeconds = (float)Math.Max(0, clip.DurationSeconds ?? 0);
        sunoPositionSeconds = 0;

        var artBytes = await FetchSunoArtworkAsync(clip, cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        var lyrics = SunoLyricsBuilder.BuildDescription(clip.LyricsText);
        var album = string.IsNullOrWhiteSpace(clip.Tags) ? "Suno" : clip.Tags;
        sunoCurrentTrack = new AudioTrackInfo(
            $"suno:{clip.Id}",
            clip.Title,
            clip.Artist,
            album,
            artBytes,
            lyrics,
            null,
            null,
            null,
            null,
            null,
            "Suno MP3",
            2,
            44100,
            16,
            TimeSpan.FromSeconds(sunoDurationSeconds));

        sunoVisualizer ??= new VisualizerSampleProvider(
            new SignalGenerator(44100, 2) { Gain = 0 });

        UpdateAlbumArt(sunoCurrentTrack);
        UpdateUiState();
    }

    private static async Task<byte[]?> FetchSunoArtworkAsync(SunoClipInfo clip, CancellationToken cancellationToken)
    {
        foreach (var url in new[] { clip.ImageLargeUrl, clip.ImageUrl }.Where(static value => !string.IsNullOrWhiteSpace(value)))
        {
            try
            {
                return await SunoMediaHttp.GetByteArrayAsync(url, cancellationToken);
            }
            catch
            {
                // Try the next cover URL.
            }
        }

        return null;
    }

    private void UpdateSunoProgress(JsonElement root)
    {
        var position = root.TryGetProperty("position", out var posEl) ? posEl.GetDouble() : sunoPositionSeconds;
        var duration = root.TryGetProperty("duration", out var durEl) ? durEl.GetDouble() : sunoDurationSeconds;
        sunoPositionSeconds = (float)Math.Max(0, position);
        UpdateSunoDuration(duration);
    }

    private void UpdateSunoDuration(double duration)
    {
        if (duration <= 0 || !double.IsFinite(duration))
        {
            return;
        }

        sunoDurationSeconds = (float)duration;
        if (sunoCurrentTrack is not null &&
            Math.Abs(sunoCurrentTrack.Duration.TotalSeconds - duration) > 0.5d)
        {
            sunoCurrentTrack = sunoCurrentTrack with { Duration = TimeSpan.FromSeconds(duration) };
        }
    }

    internal async Task SunoPlayPauseAsync()
    {
        if (sunoUsesNativePlayback)
        {
            if (engine.IsPlaying)
                engine.Pause();
            else
                engine.Play();
            SyncNativeExternalPlaybackState();
            UpdateUiState();
            await Task.CompletedTask;
            return;
        }

        if (sunoWebView?.CoreWebView2 is null) return;
        if (sunoIsPlaying)
            await sunoWebView.CoreWebView2.ExecuteScriptAsync("window.sunoPause && window.sunoPause()");
        else
            await sunoWebView.CoreWebView2.ExecuteScriptAsync("window.sunoPlay && window.sunoPlay()");
    }

    internal async Task SunoSeekAsync(float seconds)
    {
        if (sunoUsesNativePlayback)
        {
            engine.Seek(seconds);
            SyncNativeExternalPlaybackState();
            UpdateUiState();
            await Task.CompletedTask;
            return;
        }

        if (sunoWebView?.CoreWebView2 is null) return;
        await sunoWebView.CoreWebView2.ExecuteScriptAsync($"window.sunoSeek && window.sunoSeek({seconds:F3})");
        sunoPositionSeconds = seconds;
    }

    internal async Task SunoSetVolumeAsync(float volume)
    {
        if (sunoUsesNativePlayback)
        {
            engine.Volume = Math.Clamp(volume, 0, 1);
            await Task.CompletedTask;
            return;
        }

        if (sunoWebView?.CoreWebView2 is null) return;
        await sunoWebView.CoreWebView2.ExecuteScriptAsync($"window.sunoSetVolume && window.sunoSetVolume({Math.Clamp(volume, 0, 1):F3})");
    }

    internal void OnSunoNaturalEnd()
    {
        var hadNext = queue.HasNext;
        var shouldResumeSpotify = resumeSpotifyAfterLocalPlayback;

        if (hadNext || shouldResumeSpotify)
        {
            StopSunoPlayback();
        }
        else
        {
            MarkSunoEnded();
        }

        if (hadNext)
            NavigateNext();
        else if (shouldResumeSpotify)
            _ = ResumeSpotifyAfterLocalPlaybackAsync();

        UpdateUiState();
    }

    private void MarkSunoEnded()
    {
        sunoIsPlaying = false;
        if (sunoDurationSeconds > 0)
            sunoPositionSeconds = sunoDurationSeconds;
        StopSunoLoopback();
        sunoVisualizer?.Clear();
    }

    internal void StopSunoPlayback()
    {
        sunoLoadCts?.Cancel();
        if (sunoUsesNativePlayback)
        {
            engine.Unload();
            RemoteAudioCache.TryDelete(sunoTempAudioPath);
            sunoTempAudioPath = null;
        }

        if (sunoWebView?.CoreWebView2 is not null)
        {
            try { _ = sunoWebView.CoreWebView2.ExecuteScriptAsync("window.sunoStop && window.sunoStop()"); }
            catch { }
        }

        StopSunoLoopback();
        sunoUsesNativePlayback = false;
        sunoCurrentTrack = null;
        sunoCurrentClip = null;
        sunoLoadId++;
        sunoIsPlaying = false;
        sunoPositionSeconds = 0;
        sunoDurationSeconds = 0;
        sunoStatusMessage = null;
        sunoVisualizer?.Clear();
    }

    private void StopSunoLoopback()
    {
        sunoLoopback?.Stop();
        sunoLoopback?.Dispose();
        sunoLoopback = null;
    }

    private void SetSunoStatus(string message)
    {
        sunoStatusMessage = message;
        if (IsHandleCreated)
            BeginInvoke(UpdateUiState);
    }

    private void DisposeSuno()
    {
        StopSunoPlayback();
        sunoWebView?.Dispose();
        sunoWebView = null;
    }
}
