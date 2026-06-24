using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis;

public partial class Form1
{
    private AudioTrackInfo? youTubeCurrentTrack;
    private bool youTubeIsPlaying;
    private float youTubePositionSeconds;
    private float youTubeDurationSeconds;
    private string? youTubeStatusMessage;
    private CancellationTokenSource? youTubeLoadCts;

    internal static readonly string YouTubeLogPath = AppLogPaths.For("youtube.log");

    internal bool IsYouTubeActive => youTubeCurrentTrack is not null;

    internal VisualizerFrame GetYouTubeVisualizerFrame() => engine.GetVisualizerFrame();

    private void LogYouTube(string message)
    {
        try { AppLogPaths.AppendTimestamped(YouTubeLogPath, message); }
        catch { }
    }

    private static string? ExtractYouTubeVideoId(string url)
    {
        var m = Regex.Match(url, @"[?&]v=([^&#]+)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(url, @"youtu\.be/([^?&#/]+)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(url, @"/shorts/([^?&#]+)");
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(url, @"/embed/([^?&#]+)");
        if (m.Success) return m.Groups[1].Value;
        return null;
    }

    internal async Task LoadYouTubeUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        var videoId = ExtractYouTubeVideoId(url.Trim());
        LogYouTube($"--- LoadYouTubeUrlAsync: {url}  videoId={videoId ?? "(null)"}");

        if (videoId is null)
        {
            SetYouTubeStatus("YouTube: invalid URL");
            return;
        }

        if (IsSpotifyActive || spotifyService.IsLinked)
            ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
        StopRemoteAudioPlayback();
        if (IsSoundCloudActive)
            StopSoundCloudPlayback();
        if (IsSunoActive)
            StopSunoPlayback();
        StopYouTubePlayback();

        StopLocalPlaybackForExternalUrl();
        if (engine.IsLoaded)
            engine.Unload();

        var cts = youTubeLoadCts = new CancellationTokenSource();

        var ytDlp = YtDlpService.FindExecutable();
        if (ytDlp is null)
        {
            LogYouTube("yt-dlp not found on PATH, WinGet, or app directory");
            SetYouTubeStatus("YouTube: install yt-dlp to enable streaming");
            return;
        }

        LogYouTube($"yt-dlp: {ytDlp}");
        SetYouTubeStatus("YouTube: fetching stream...");
        UpdateUiState();

        var videoUrl = $"https://www.youtube.com/watch?v={videoId}";

        try
        {
            using var http = new HttpClient();
            var streamTask = Task.Run(() => YtDlpService.GetStreamUrlAsync(ytDlp, videoUrl, cts.Token), cts.Token);
            var oembedTask = FetchYouTubeOEmbedAsync(videoId, http, cts.Token);
            var artTask    = FetchYouTubeArtworkAsync(videoId, http, cts.Token);

            await Task.WhenAll(streamTask, oembedTask, artTask);

            if (cts.IsCancellationRequested) return;

            var streamUrl      = await streamTask;
            var (title, artist) = await oembedTask;
            var artBytes       = await artTask;

            if (string.IsNullOrWhiteSpace(title)) title = videoId;

            LogYouTube($"stream ready  title=\"{title}\" artist=\"{artist}\" urlLen={streamUrl.Length}");
            SetYouTubeStatus("YouTube: buffering...");
            UpdateUiState();

            var prebuiltTrack = new AudioTrackInfo(
                $"youtube:{videoId}",
                title, artist, null, artBytes,
                null, null, null, null, null, null,
                "YouTube", 2, 44100, 16, TimeSpan.Zero);

            await Task.Run(() =>
            {
                if (!cts.IsCancellationRequested)
                    engine.Load(streamUrl, prebuiltTrack);
            }, cts.Token);

            if (cts.IsCancellationRequested) return;

            youTubeCurrentTrack  = engine.CurrentTrack ?? prebuiltTrack;
            youTubePositionSeconds = 0;
            youTubeDurationSeconds = engine.GetLength();
            youTubeStatusMessage   = null;

            engine.Volume = trackBarVolume.Value / 100f;
            engine.Play();
            youTubeIsPlaying = true;

            UpdateAlbumArt(youTubeCurrentTrack);
            UpdateUiState();

            LogYouTube($"playback started  dur={youTubeDurationSeconds:F1}s");
        }
        catch (OperationCanceledException)
        {
            LogYouTube("load cancelled");
        }
        catch (Exception ex)
        {
            LogYouTube($"load failed: {ex.Message}");
            SetYouTubeStatus($"YouTube: {ex.Message}");
            youTubeCurrentTrack = null;
            UpdateUiState();
        }
    }

    internal void OnYouTubeNaturalEnd()
    {
        var hadNext = queue.HasNext;
        var shouldResumeSpotify = resumeSpotifyAfterLocalPlayback;

        if (hadNext || shouldResumeSpotify)
        {
            StopYouTubePlayback();
        }
        else
        {
            youTubeIsPlaying = false;
            if (youTubeDurationSeconds > 0)
                youTubePositionSeconds = youTubeDurationSeconds;
        }

        if (hadNext)
            NavigateNext();
        else if (shouldResumeSpotify)
            _ = ResumeSpotifyAfterLocalPlaybackAsync();

        UpdateUiState();
    }

    internal async Task YouTubePlayPauseAsync()
    {
        if (youTubeIsPlaying)
            engine.Pause();
        else
            engine.Play();

        youTubeIsPlaying = engine.IsPlaying;
        youTubePositionSeconds = engine.GetPosition();

        if (youTubeVideoMode)
            await SyncYouTubeVideoFrameAsync(youTubePositionSeconds, youTubeIsPlaying);

        UpdateUiState();
    }

    internal async Task YouTubeSeekAsync(float seconds)
    {
        engine.Seek(Math.Clamp(seconds, 0, youTubeDurationSeconds));
        youTubePositionSeconds = engine.GetPosition();

        if (youTubeVideoMode)
            await SyncYouTubeVideoFrameAsync(youTubePositionSeconds, engine.IsPlaying);
    }

    internal async Task YouTubeSetVolumeAsync(float volume)
    {
        engine.Volume = Math.Clamp(volume, 0f, 1f);

        if (youTubeVideoMode)
            await SyncYouTubeVideoFrameAsync(engine.GetPosition(), engine.IsPlaying);
    }

    internal void StopYouTubePlayback()
    {
        StopYouTubeVideoModeSync();

        if (youTubeLoadCts is not null)
        {
            youTubeLoadCts.Cancel();
            youTubeLoadCts.Dispose();
            youTubeLoadCts = null;
        }

        if (youTubeCurrentTrack is not null)
            engine.Unload();

        youTubeCurrentTrack  = null;
        youTubeIsPlaying     = false;
        youTubePositionSeconds = 0;
        youTubeDurationSeconds = 0;
        youTubeStatusMessage   = null;
    }

    private void SetYouTubeStatus(string message)
    {
        youTubeStatusMessage = message;
        if (IsHandleCreated)
            BeginInvoke(UpdateUiState);
    }

    private void DisposeYouTube()
    {
        StopYouTubePlayback();
    }

    private async Task<(string Title, string? Artist)> FetchYouTubeOEmbedAsync(
        string videoId, HttpClient http, CancellationToken ct)
    {
        try
        {
            var json = await http.GetStringAsync(
                $"https://www.youtube.com/oembed?url=https://www.youtube.com/watch?v={videoId}&format=json", ct);
            using var doc = JsonDocument.Parse(json);
            var t = doc.RootElement.TryGetProperty("title",       out var te) ? te.GetString() ?? "" : "";
            var a = doc.RootElement.TryGetProperty("author_name", out var ae) ? ae.GetString()       : null;
            return (t, a);
        }
        catch (Exception ex)
        {
            LogYouTube($"oEmbed failed: {ex.Message}");
            return ("", null);
        }
    }

    private async Task<byte[]?> FetchYouTubeArtworkAsync(string videoId, HttpClient http, CancellationToken ct)
    {
        var maxResUrl = $"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg";
        var hqUrl     = $"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg";
        try
        {
            var bytes = await http.GetByteArrayAsync(maxResUrl, ct);
            if (bytes.Length > 5000) return bytes;
        }
        catch { }
        try { return await http.GetByteArrayAsync(hqUrl, ct); }
        catch { return null; }
    }
}
