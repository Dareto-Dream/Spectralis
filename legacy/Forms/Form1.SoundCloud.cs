using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using NAudio.Wave.SampleProviders;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis;

public partial class Form1
{
    private static readonly HttpClient SoundCloudHttp = CreateSoundCloudHttp();
    private static readonly SemaphoreSlim SoundCloudClientIdLock = new(1, 1);
    private static string? cachedSoundCloudClientId;

    private WebView2? soundCloudWebView;
    private Task? soundCloudPrewarmTask;
    private VisualizerSampleProvider? soundCloudVisualizer;
    private SpotifyLoopbackCapture? soundCloudLoopback;
    private AudioTrackInfo? soundCloudCurrentTrack;
    private CancellationTokenSource? soundCloudLoadCts;
    private string? soundCloudTempAudioPath;
    private bool soundCloudUsesNativePlayback;
    private bool soundCloudIsPlaying;
    private float soundCloudPositionSeconds;
    private float soundCloudDurationSeconds;
    private string? soundCloudStatusMessage;
    private CancellationTokenSource? soundCloudArtCts;
    private int soundCloudLoadId;

    internal static readonly string SoundCloudLogPath =
        AppLogPaths.For("soundcloud.log");

    internal bool IsSoundCloudActive => soundCloudCurrentTrack is not null;

    internal VisualizerFrame GetSoundCloudVisualizerFrame() =>
        soundCloudUsesNativePlayback
            ? engine.GetVisualizerFrame()
            : soundCloudVisualizer?.GetFrame() ?? VisualizerFrame.Empty;

    private void LogSoundCloud(string message)
    {
        try { AppLogPaths.AppendTimestamped(SoundCloudLogPath, message); }
        catch { }
    }

    private static HttpClient CreateSoundCloudHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,*/*");
        return http;
    }

    internal async Task LoadSoundCloudUrlAsync(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return;

        LogSoundCloud($"--- LoadSoundCloudUrlAsync: {url}");

        if (IsSpotifyActive || spotifyService.IsLinked)
            ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
        StopRemoteAudioPlayback();
        StopLocalPlaybackForExternalUrl();
        if (IsYouTubeActive)
            StopYouTubePlayback();
        if (IsSunoActive)
            StopSunoPlayback();

        StopSoundCloudPlayback();
        var loadId = ++soundCloudLoadId;
        var cts = soundCloudLoadCts = new CancellationTokenSource();
        SetSoundCloudStatus("SoundCloud loading...");
        UpdateUiState();

        var playbackSource = await ResolveSoundCloudPlaybackSourceAsync(url.Trim());
        if (cts.IsCancellationRequested || loadId != soundCloudLoadId)
        {
            LogSoundCloud($"Load {loadId} superseded before native resolve");
            return;
        }

        if (await TryStartNativeSoundCloudPlaybackAsync(playbackSource, loadId, cts.Token))
            return;

        if (cts.IsCancellationRequested || loadId != soundCloudLoadId)
        {
            LogSoundCloud($"Load {loadId} superseded before widget fallback");
            return;
        }

        await StartSoundCloudWidgetPlaybackAsync(playbackSource, loadId);
    }

    private async Task StartSoundCloudWidgetPlaybackAsync(SoundCloudPlaybackSource playbackSource, int loadId)
    {
        if (soundCloudWebView is null)
        {
            // Await the background pre-warm if it's still in progress
            LogSoundCloud("WebView2 not ready, awaiting prewarm");
            if (soundCloudPrewarmTask is not null)
            {
                try { await soundCloudPrewarmTask; }
                catch (Exception ex) { LogSoundCloud($"Prewarm failed: {ex.Message}"); }
                soundCloudPrewarmTask = null;
            }

            if (soundCloudWebView is null)
                await InitializeSoundCloudAsync(reportFailure: true);
        }

        if (soundCloudWebView?.CoreWebView2 is null)
        {
            LogSoundCloud("WebView2 unavailable");
            SetSoundCloudStatus("SoundCloud unavailable");
            return;
        }

        var urlJson = JsonSerializer.Serialize(playbackSource.Url);
        var secretTokenJson = JsonSerializer.Serialize(playbackSource.SecretToken);
        soundCloudUsesNativePlayback = false;
        LogSoundCloud($"Calling window.scLoad loadId={loadId} url={playbackSource.Url} secretToken={(playbackSource.SecretToken is null ? "(none)" : "(present)")}");
        try
        {
            var result = await soundCloudWebView.CoreWebView2.ExecuteScriptAsync($"window.scLoad && window.scLoad({urlJson}, {loadId}, {secretTokenJson})");
            LogSoundCloud($"scLoad script result: {result}");
        }
        catch (Exception ex)
        {
            LogSoundCloud($"scLoad script exception: {ex}");
            SetSoundCloudStatus("SoundCloud failed to load");
        }
    }

    private async Task<SoundCloudPlaybackSource> ResolveSoundCloudPlaybackSourceAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            LogSoundCloud("URL is not absolute; passing through to widget");
            return new SoundCloudPlaybackSource(url, null);
        }

        if (!ShouldResolveSoundCloudRedirect(uri))
        {
            var normalizedUrl = NormalizeSoundCloudPlaybackUrl(uri);
            if (!string.Equals(normalizedUrl, url, StringComparison.Ordinal))
                LogSoundCloud($"Normalized SoundCloud URL: {normalizedUrl}");
            return await ResolveSoundCloudOEmbedSourceAsync(normalizedUrl) ??
                new SoundCloudPlaybackSource(normalizedUrl, null);
        }

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };

            for (var redirect = 0; redirect < 8; redirect++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.UserAgent.ParseAdd(
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
                request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");

                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                if (!IsRedirectStatusCode(response.StatusCode) || response.Headers.Location is null)
                {
                    LogSoundCloud($"Short-link resolve stopped with HTTP {(int)response.StatusCode}");
                    break;
                }

                var nextUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(uri, response.Headers.Location);

                LogSoundCloud($"Resolved redirect {redirect + 1}: {uri} -> {nextUri}");

                if (!IsSoundCloudHost(nextUri))
                {
                    LogSoundCloud($"Ignoring non-SoundCloud redirect target: {nextUri}");
                    break;
                }

                uri = nextUri;
                if (!ShouldResolveSoundCloudRedirect(uri))
                    break;
            }
        }
        catch (Exception ex)
        {
            LogSoundCloud($"Short-link resolve failed: {ex.Message}");
            return new SoundCloudPlaybackSource(url, null);
        }

        var playbackUrl = NormalizeSoundCloudPlaybackUrl(uri);
        return await ResolveSoundCloudOEmbedSourceAsync(playbackUrl) ??
            new SoundCloudPlaybackSource(playbackUrl, null);
    }

    private async Task<SoundCloudPlaybackSource?> ResolveSoundCloudOEmbedSourceAsync(string url)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var oEmbedUrl = $"https://soundcloud.com/oembed?format=json&url={Uri.EscapeDataString(url)}";
            var json = await http.GetStringAsync(oEmbedUrl);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("html", out var htmlElement))
            {
                LogSoundCloud("oEmbed response did not include html");
                return null;
            }

            var html = htmlElement.GetString();
            var source = TryExtractSoundCloudWidgetSource(html);
            if (source is null)
            {
                LogSoundCloud("oEmbed html did not include a usable widget source");
                return null;
            }

            LogSoundCloud($"oEmbed resolved widget target url={source.Url} secretToken={(source.SecretToken is null ? "(none)" : "(present)")}");
            return source;
        }
        catch (Exception ex)
        {
            LogSoundCloud($"oEmbed resolve failed: {ex.Message}");
            return null;
        }
    }

    private static SoundCloudPlaybackSource? TryExtractSoundCloudWidgetSource(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;

        var match = Regex.Match(html, """src\s*=\s*["'](?<src>[^"']+)["']""", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        var source = WebUtility.HtmlDecode(match.Groups["src"].Value);
        if (!Uri.TryCreate(source, UriKind.Absolute, out var widgetUri))
            return null;

        var query = ParseQueryParameters(widgetUri.Query);
        if (!query.TryGetValue("url", out var targetUrl) || string.IsNullOrWhiteSpace(targetUrl))
            return null;

        query.TryGetValue("secret_token", out var secretToken);
        return new SoundCloudPlaybackSource(targetUrl, string.IsNullOrWhiteSpace(secretToken) ? null : secretToken);
    }

    private static bool ShouldResolveSoundCloudRedirect(Uri uri) =>
        uri.Scheme is "http" or "https" &&
        (uri.Host.Equals("on.soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("snd.sc", StringComparison.OrdinalIgnoreCase));

    private static bool IsSoundCloudHost(Uri uri) =>
        uri.Host.Equals("soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.EndsWith(".soundcloud.com", StringComparison.OrdinalIgnoreCase) ||
        uri.Host.Equals("snd.sc", StringComparison.OrdinalIgnoreCase);

    private static bool IsRedirectStatusCode(HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static string NormalizeSoundCloudPlaybackUrl(Uri uri)
    {
        if (!IsSoundCloudHost(uri) || string.IsNullOrEmpty(uri.Query))
            return uri.ToString();

        var queryParts = uri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        var keptParts = queryParts
            .Where(part =>
            {
                var nameEnd = part.IndexOf('=');
                var encodedName = nameEnd >= 0 ? part[..nameEnd] : part;
                var name = Uri.UnescapeDataString(encodedName);
                return !name.Equals("si", StringComparison.OrdinalIgnoreCase) &&
                    !name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase);
            })
            .ToArray();

        if (keptParts.Length == queryParts.Length)
            return uri.ToString();

        var builder = new UriBuilder(uri)
        {
            Query = string.Join("&", keptParts)
        };
        return builder.Uri.ToString();
    }

    private static Dictionary<string, string> ParseQueryParameters(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = part.IndexOf('=');
            var rawName = separatorIndex >= 0 ? part[..separatorIndex] : part;
            var rawValue = separatorIndex >= 0 ? part[(separatorIndex + 1)..] : "";
            var name = Uri.UnescapeDataString(rawName.Replace("+", " "));
            var value = Uri.UnescapeDataString(rawValue.Replace("+", " "));
            if (!string.IsNullOrWhiteSpace(name))
                result[name] = value;
        }

        return result;
    }

    private sealed record SoundCloudPlaybackSource(string Url, string? SecretToken);

    private sealed record SoundCloudNativeTrack(
        string Id,
        string Title,
        string? Artist,
        string? Genre,
        string? ArtworkUrl,
        double DurationMs,
        string StreamUrl,
        string? Description);

    private async Task<bool> TryStartNativeSoundCloudPlaybackAsync(
        SoundCloudPlaybackSource playbackSource,
        int loadId,
        CancellationToken cancellationToken)
    {
        string? tempPath = null;
        try
        {
            var nativeTrack = await ResolveSoundCloudNativeTrackAsync(playbackSource, cancellationToken);
            if (nativeTrack is null)
                return false;

            if (cancellationToken.IsCancellationRequested || loadId != soundCloudLoadId)
                return true;

            SetSoundCloudStatus("SoundCloud buffering...");
            tempPath = await RemoteAudioCache.DownloadAsync(nativeTrack.StreamUrl, ".mp3", cancellationToken);
            if (cancellationToken.IsCancellationRequested || loadId != soundCloudLoadId)
            {
                RemoteAudioCache.TryDelete(tempPath);
                return true;
            }

            var artBytes = await FetchSoundCloudArtworkBytesAsync(nativeTrack.ArtworkUrl, cancellationToken);
            if (cancellationToken.IsCancellationRequested || loadId != soundCloudLoadId)
            {
                RemoteAudioCache.TryDelete(tempPath);
                return true;
            }

            var lyrics = BuildSoundCloudDescriptionLyrics(nativeTrack.Description, nativeTrack.DurationMs / 1000.0);
            var trackInfo = new AudioTrackInfo(
                $"soundcloud:{nativeTrack.Id}",
                nativeTrack.Title,
                nativeTrack.Artist,
                nativeTrack.Genre,
                artBytes,
                lyrics,
                null,
                null,
                null,
                null,
                null,
                "SoundCloud MP3",
                2,
                44100,
                16,
                TimeSpan.FromMilliseconds(nativeTrack.DurationMs));

            engine.Load(tempPath, trackInfo);
            engine.Volume = trackBarVolume.Value / 100f;
            soundCloudTempAudioPath = tempPath;
            soundCloudUsesNativePlayback = true;
            soundCloudCurrentTrack = engine.CurrentTrack ?? trackInfo;
            soundCloudPositionSeconds = 0;
            soundCloudDurationSeconds = engine.GetLength() > 0
                ? engine.GetLength()
                : (float)(nativeTrack.DurationMs / 1000.0);
            soundCloudStatusMessage = null;

            engine.Play();
            SyncNativeExternalPlaybackState();
            UpdateAlbumArt(soundCloudCurrentTrack);
            UpdateUiState();
            LogSoundCloud($"Native playback started from cached MP3: {tempPath}");
            return true;
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(tempPath);
            return true;
        }
        catch (Exception ex)
        {
            LogSoundCloud($"Native playback failed, falling back to widget: {ex.Message}");
            RemoteAudioCache.TryDelete(tempPath);
            soundCloudUsesNativePlayback = false;
            return false;
        }
    }

    private async Task<SoundCloudNativeTrack?> ResolveSoundCloudNativeTrackAsync(
        SoundCloudPlaybackSource playbackSource,
        CancellationToken cancellationToken)
    {
        var clientId = await GetSoundCloudClientIdAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(clientId))
        {
            LogSoundCloud("Native resolve skipped: no SoundCloud client_id");
            return null;
        }

        using var trackDoc = await FetchSoundCloudTrackJsonAsync(playbackSource, clientId, cancellationToken);
        if (trackDoc is null)
            return null;

        var root = trackDoc.RootElement;
        if (root.TryGetProperty("kind", out var kindEl) &&
            !string.Equals(kindEl.GetString(), "track", StringComparison.OrdinalIgnoreCase))
        {
            LogSoundCloud($"Native resolve skipped: unsupported kind {kindEl.GetString() ?? "(null)"}");
            return null;
        }

        if (root.TryGetProperty("streamable", out var streamableEl) &&
            streamableEl.ValueKind == JsonValueKind.False)
        {
            LogSoundCloud("Native resolve skipped: track is not streamable");
            return null;
        }

        var transcodingUrl = TryGetSoundCloudProgressiveTranscodingUrl(root);
        if (string.IsNullOrWhiteSpace(transcodingUrl))
        {
            LogSoundCloud("Native resolve skipped: no progressive MP3 transcoding");
            return null;
        }

        var streamUrl = await ResolveSoundCloudStreamUrlAsync(transcodingUrl, clientId, cancellationToken);
        if (string.IsNullOrWhiteSpace(streamUrl))
            return null;

        var id = TryGetJsonInt64(root, "id")?.ToString() ?? TryExtractSoundCloudTrackId(playbackSource.Url) ?? "track";
        var title = TryGetJsonString(root, "title");
        if (string.IsNullOrWhiteSpace(title))
            title = "SoundCloud track";

        var artist = TryGetNestedJsonString(root, "user", "username");
        var publisherArtist = TryGetNestedJsonString(root, "publisher_metadata", "artist");
        if (!string.IsNullOrWhiteSpace(publisherArtist))
            artist = publisherArtist;

        var durationMs = TryGetJsonDouble(root, "full_duration") ??
            TryGetJsonDouble(root, "duration") ??
            0;

        return new SoundCloudNativeTrack(
            id,
            title,
            artist,
            TryGetJsonString(root, "genre"),
            TryGetJsonString(root, "artwork_url"),
            durationMs,
            streamUrl,
            TryGetJsonString(root, "description"));
    }

    private async Task<JsonDocument?> FetchSoundCloudTrackJsonAsync(
        SoundCloudPlaybackSource playbackSource,
        string clientId,
        CancellationToken cancellationToken)
    {
        var trackId = TryExtractSoundCloudTrackId(playbackSource.Url);
        var apiUrl = trackId is not null
            ? AppendQueryParameter($"https://api-v2.soundcloud.com/tracks/{trackId}", "client_id", clientId)
            : AppendQueryParameter(
                "https://api-v2.soundcloud.com/resolve",
                "url",
                playbackSource.Url);

        if (trackId is not null && !string.IsNullOrWhiteSpace(playbackSource.SecretToken))
            apiUrl = AppendQueryParameter(apiUrl, "secret_token", playbackSource.SecretToken);

        if (trackId is null)
            apiUrl = AppendQueryParameter(apiUrl, "client_id", clientId);

        try
        {
            var json = await SoundCloudHttp.GetStringAsync(apiUrl, cancellationToken);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSoundCloud($"Native track API failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> ResolveSoundCloudStreamUrlAsync(
        string transcodingUrl,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = AppendQueryParameter(transcodingUrl, "client_id", clientId);
            var json = await SoundCloudHttp.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);
            var streamUrl = TryGetJsonString(doc.RootElement, "url");
            if (string.IsNullOrWhiteSpace(streamUrl))
            {
                LogSoundCloud("Native stream API did not return a URL");
                return null;
            }

            return streamUrl;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSoundCloud($"Native stream API failed: {ex.Message}");
            return null;
        }
    }

    private async Task<string?> GetSoundCloudClientIdAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(cachedSoundCloudClientId))
            return cachedSoundCloudClientId;

        await SoundCloudClientIdLock.WaitAsync(cancellationToken);
        try
        {
            if (!string.IsNullOrWhiteSpace(cachedSoundCloudClientId))
                return cachedSoundCloudClientId;

            var html = await SoundCloudHttp.GetStringAsync("https://soundcloud.com/", cancellationToken);
            var assets = Regex.Matches(
                    html,
                    """https://a-v2\.sndcdn\.com/assets/[^"']+\.js""",
                    RegexOptions.IgnoreCase)
                .Cast<Match>()
                .Select(match => match.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            foreach (var asset in assets)
            {
                string script;
                try
                {
                    script = await SoundCloudHttp.GetStringAsync(asset, cancellationToken);
                }
                catch
                {
                    continue;
                }

                var match = Regex.Match(
                    script,
                    """client_id\s*:\s*["'](?<id>[A-Za-z0-9_-]{16,})["']""",
                    RegexOptions.IgnoreCase);
                if (!match.Success)
                    continue;

                cachedSoundCloudClientId = match.Groups["id"].Value;
                LogSoundCloud("Discovered SoundCloud client_id from web assets");
                return cachedSoundCloudClientId;
            }

            LogSoundCloud("SoundCloud client_id was not found in web assets");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSoundCloud($"SoundCloud client_id discovery failed: {ex.Message}");
            return null;
        }
        finally
        {
            SoundCloudClientIdLock.Release();
        }
    }

    private static string? TryGetSoundCloudProgressiveTranscodingUrl(JsonElement root)
    {
        if (!root.TryGetProperty("media", out var mediaEl) ||
            !mediaEl.TryGetProperty("transcodings", out var transcodingsEl) ||
            transcodingsEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var transcoding in transcodingsEl.EnumerateArray())
        {
            if (!transcoding.TryGetProperty("format", out var formatEl))
                continue;

            var protocol = TryGetJsonString(formatEl, "protocol");
            var mimeType = TryGetJsonString(formatEl, "mime_type");
            if (!string.Equals(protocol, "progressive", StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(mimeType, "audio/mpeg", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var url = TryGetJsonString(transcoding, "url");
            if (!string.IsNullOrWhiteSpace(url))
                return url;
        }

        return null;
    }

    private static string? TryExtractSoundCloudTrackId(string url)
    {
        var match = Regex.Match(
            url,
            @"/tracks/(?:soundcloud%3Atracks%3A)?(?<id>\d+)",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static string AppendQueryParameter(string url, string name, string value)
    {
        var separator = url.Contains('?') ? '&' : '?';
        return $"{url}{separator}{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}";
    }

    private static string? TryGetJsonString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : value.ToString();
    }

    private static string? TryGetNestedJsonString(JsonElement root, string objectName, string propertyName) =>
        root.TryGetProperty(objectName, out var objectEl) && objectEl.ValueKind == JsonValueKind.Object
            ? TryGetJsonString(objectEl, propertyName)
            : null;

    private static double? TryGetJsonDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetDouble(out var result) ? result : null;
    }

    private static long? TryGetJsonInt64(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.TryGetInt64(out var result) ? result : null;
    }

    private async Task<byte[]?> FetchSoundCloudArtworkBytesAsync(
        string? artworkUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(artworkUrl))
            return null;

        var urls = new[]
        {
            artworkUrl.Replace("-large.", "-t500x500."),
            artworkUrl
        };

        foreach (var url in urls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                return await SoundCloudHttp.GetByteArrayAsync(url, cancellationToken);
            }
            catch
            {
                // Try the next cover URL.
            }
        }

        return null;
    }

    private async Task InitializeSoundCloudAsync(bool reportFailure = false)
    {
        if (soundCloudWebView is not null) return;

        LogSoundCloud("InitializeSoundCloudAsync start");
        try
        {
            soundCloudWebView = new WebView2
            {
                Enabled = false,
                Location = new System.Drawing.Point(-20, -20),
                Size = new System.Drawing.Size(2, 2),
                TabStop = false,
                Visible = true
            };
            Controls.Add(soundCloudWebView);
            soundCloudWebView.SendToBack();

            await soundCloudWebView.EnsureCoreWebView2Async(null);
            soundCloudWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            soundCloudWebView.CoreWebView2.WebMessageReceived += OnSoundCloudWebMessage;

            soundCloudWebView.CoreWebView2.NavigateToString(BuildSoundCloudHtml());
            LogSoundCloud("InitializeSoundCloudAsync complete");
        }
        catch (Exception ex)
        {
            LogSoundCloud($"InitializeSoundCloudAsync FAILED: {ex}");
            soundCloudWebView?.Dispose();
            soundCloudWebView = null;
            if (reportFailure)
                SetSoundCloudStatus("SoundCloud unavailable");
        }
    }

    private static string BuildSoundCloudHtml() => """
        <!DOCTYPE html>
        <html><body style="margin:0;padding:0;">
        <iframe id="sc-widget" allow="autoplay" style="position:absolute;left:-9999px;top:-9999px;width:1px;height:1px;" frameborder="0"></iframe>
        <script src="https://w.soundcloud.com/player/api.js"></script>
        <script>
        const post = (loadId, p) => {
            p.loadId = loadId;
            window.chrome.webview.postMessage(JSON.stringify(p));
        };
        const log = message => post(loadGeneration, { type: 'log', message });
        let widget = null;
        let trackReady = false;
        let loadGeneration = 0;
        let resolveTimer = 0;

        window.addEventListener('error', e => log('window error: ' + (e.message || e.error || 'unknown')));
        window.addEventListener('unhandledrejection', e => {
            const reason = e.reason && (e.reason.message || e.reason);
            log('unhandled rejection: ' + (reason || 'unknown'));
        });

        const widgetOptions = {
            auto_play: false,
            buying: false,
            liking: false,
            download: false,
            sharing: false,
            show_artwork: false,
            show_comments: false,
            show_playcount: false,
            show_user: false,
            hide_related: true,
            visual: false
        };

        function buildWidgetUrl(url, secretToken) {
            return 'https://w.soundcloud.com/player/?url=' + encodeURIComponent(url)
                + (secretToken ? '&secret_token=' + encodeURIComponent(secretToken) : '')
                + '&auto_play=false&buying=false&liking=false&download=false&sharing=false'
                + '&show_artwork=false&show_comments=false&show_playcount=false'
                + '&show_user=false&hide_related=true&visual=false';
        }

        function applySound(loadId, sound) {
            if (loadId !== loadGeneration || !sound) return;
            trackReady = true;
            log('resolved sound title="' + (sound.title || '') + '" duration=' + (sound.duration || 0));
            post(loadId, {
                type: 'track_loaded',
                title: sound.title || '',
                artist: (sound.user && sound.user.username) || '',
                artworkUrl: sound.artwork_url || null,
                duration: sound.duration || 0,
                description: sound.description || null
            });
            widget.play();
        }

        function resolveSound(loadId, attempt) {
            if (loadId !== loadGeneration || trackReady || !widget) return;

            widget.getCurrentSound(sound => {
                if (loadId !== loadGeneration || trackReady) return;
                if (sound) {
                    log('getCurrentSound succeeded on attempt ' + attempt);
                    applySound(loadId, sound);
                    return;
                }

                widget.getSounds(sounds => {
                    if (loadId !== loadGeneration || trackReady) return;
                    const fallbackSound = Array.isArray(sounds) && sounds.length > 0 ? sounds[0] : null;
                    if (fallbackSound) {
                        log('getSounds fallback succeeded on attempt ' + attempt + ' count=' + sounds.length);
                        applySound(loadId, fallbackSound);
                        return;
                    }

                    if (attempt === 0 || attempt === 4 || attempt === 16 || attempt === 32) {
                        log('sound not resolved yet attempt=' + attempt + ' sounds=' + (Array.isArray(sounds) ? sounds.length : 'null'));
                    }

                    if (attempt < 32) {
                        resolveTimer = window.setTimeout(() => resolveSound(loadId, attempt + 1), 250);
                    } else {
                        log('resolve failed after retries');
                        post(loadId, { type: 'error', message: "Track can't be played. It may be unavailable, private, or disabled for embeds." });
                    }
                });
            });
        }

        function beginResolve(loadId) {
            if (loadId !== loadGeneration || trackReady) return;
            window.clearTimeout(resolveTimer);
            resolveTimer = window.setTimeout(() => resolveSound(loadId, 0), 0);
        }

        function bindEvents() {
            widget.bind(SC.Widget.Events.READY, () => {
                log('SC.Widget READY');
                beginResolve(loadGeneration);
            });
            widget.bind(SC.Widget.Events.PLAY,   () => { if (trackReady) { log('SC.Widget PLAY'); post(loadGeneration, { type: 'play' }); } });
            widget.bind(SC.Widget.Events.PAUSE,  () => { if (trackReady) { log('SC.Widget PAUSE'); post(loadGeneration, { type: 'pause' }); } });
            widget.bind(SC.Widget.Events.FINISH, () => { log('SC.Widget FINISH'); post(loadGeneration, { type: 'finish' }); });
            widget.bind(SC.Widget.Events.PLAY_PROGRESS, e => {
                post(loadGeneration, { type: 'progress', position: e.currentPosition, duration: e.soundDuration });
            });
            widget.bind(SC.Widget.Events.ERROR, () => {
                log('SC.Widget ERROR trackReady=' + trackReady);
                if (trackReady) {
                    post(loadGeneration, { type: 'error', message: 'Playback failed' });
                } else {
                    beginResolve(loadGeneration);
                }
            });
        }

        window.scLoad = (url, loadId, secretToken) => {
            loadGeneration = Number(loadId) || (loadGeneration + 1);
            trackReady = false;
            window.clearTimeout(resolveTimer);
            log('scLoad url=' + url + ' loadId=' + loadGeneration + ' secretToken=' + Boolean(secretToken) + ' hasWidget=' + Boolean(widget));
            if (!window.SC || !SC.Widget) {
                log('SoundCloud widget API is unavailable');
                post(loadGeneration, { type: 'error', message: 'SoundCloud widget API did not load' });
                return 'missing-api';
            }

            const iframe = document.getElementById('sc-widget');
            if (!widget) {
                iframe.src = buildWidgetUrl(url, secretToken);
                widget = SC.Widget(iframe);
                bindEvents();
            } else {
                const options = { ...widgetOptions, callback: () => { log('widget.load callback'); beginResolve(loadGeneration); } };
                if (secretToken) options.secret_token = secretToken;
                widget.load(url, options);
            }
        };

        window.scPlay  = () => widget && widget.play();
        window.scPause = () => widget && widget.pause();
        window.scSeek  = ms => widget && widget.seekTo(ms);
        </script>
        </body></html>
        """;

    private async void OnSoundCloudWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.TryGetWebMessageAsString());
            var root = doc.RootElement;
            if (root.TryGetProperty("loadId", out var loadIdEl) &&
                loadIdEl.ValueKind == JsonValueKind.Number &&
                loadIdEl.TryGetInt32(out var messageLoadId) &&
                messageLoadId != soundCloudLoadId)
            {
                return;
            }

            var type = root.GetProperty("type").GetString();

            switch (type)
            {
                case "log":
                    var logMessage = root.TryGetProperty("message", out var logEl) ? logEl.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(logMessage))
                        LogSoundCloud($"[JS] {logMessage}");
                    break;

                case "track_loaded":
                    soundCloudStatusMessage = null;
                    var title = root.TryGetProperty("title", out var titleEl) ? titleEl.GetString() ?? "" : "";
                    var artist = root.TryGetProperty("artist", out var artistEl) ? artistEl.GetString() : null;
                    var artworkUrl = root.TryGetProperty("artworkUrl", out var artEl) ? artEl.GetString() : null;
                    var durationMs = root.TryGetProperty("duration", out var durEl) ? durEl.GetDouble() : 0;
                    var description = root.TryGetProperty("description", out var descEl) ? descEl.GetString() : null;
                    LogSoundCloud($"track_loaded title=\"{title}\" artist=\"{artist}\" durationMs={durationMs}");
                    await ApplySoundCloudTrackAsync(title, artist, artworkUrl, durationMs, description);
                    break;

                case "play":
                    LogSoundCloud("play");
                    soundCloudIsPlaying = true;
                    if (soundCloudLoopback is null && soundCloudVisualizer is not null)
                    {
                        soundCloudLoopback = new SpotifyLoopbackCapture();
                        soundCloudLoopback.Start(soundCloudVisualizer);
                    }
                    UpdateUiState();
                    break;

                case "pause":
                    LogSoundCloud("pause");
                    soundCloudIsPlaying = false;
                    soundCloudLoopback?.Stop();
                    soundCloudLoopback?.Dispose();
                    soundCloudLoopback = null;
                    UpdateUiState();
                    break;

                case "finish":
                    LogSoundCloud("finish");
                    OnSoundCloudNaturalEnd();
                    break;

                case "progress":
                    var posMs = root.TryGetProperty("position", out var posEl) ? posEl.GetDouble() : 0;
                    var durMs2 = root.TryGetProperty("duration", out var dur2El) ? dur2El.GetDouble() : 0;
                    soundCloudPositionSeconds = (float)(posMs / 1000.0);
                    if (durMs2 > 0) soundCloudDurationSeconds = (float)(durMs2 / 1000.0);
                    break;

                case "error":
                    var msg = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() : null;
                    LogSoundCloud($"error: {msg ?? "(null)"}");
                    StopSoundCloudPlayback();
                    SetSoundCloudStatus(string.IsNullOrWhiteSpace(msg) ? "SoundCloud error" : $"SoundCloud: {msg}");
                    break;
            }
        }
        catch (Exception ex)
        {
            LogSoundCloud($"OnSoundCloudWebMessage exception: {ex.Message}");
        }
    }

    private async Task ApplySoundCloudTrackAsync(string title, string? artist, string? artworkUrl, double durationMs, string? description = null)
    {
        soundCloudArtCts?.Cancel();
        var cts = soundCloudArtCts = new CancellationTokenSource();
        soundCloudDurationSeconds = (float)(durationMs / 1000.0);

        byte[]? artBytes = null;
        if (artworkUrl is not null)
        {
            var hiResUrl = artworkUrl.Replace("-large.", "-t500x500.");
            try
            {
                using var http = new HttpClient();
                artBytes = await http.GetByteArrayAsync(hiResUrl, cts.Token);
            }
            catch
            {
                try
                {
                    using var http = new HttpClient();
                    artBytes = await http.GetByteArrayAsync(artworkUrl, cts.Token);
                }
                catch { }
            }
        }

        if (cts.IsCancellationRequested) return;

        var lyrics = BuildSoundCloudDescriptionLyrics(description, durationMs / 1000.0);
        soundCloudCurrentTrack = new AudioTrackInfo(
            $"soundcloud:{title}",
            title, artist, null, artBytes,
            lyrics, null, null, null, null, null,
            "SoundCloud", 2, 44100, 16,
            TimeSpan.FromMilliseconds(durationMs));

        soundCloudVisualizer ??= new VisualizerSampleProvider(
            new SignalGenerator(44100, 2) { Gain = 0 });

        UpdateAlbumArt(soundCloudCurrentTrack);
        UpdateUiState();
    }

    internal async Task SoundCloudPlayPauseAsync()
    {
        if (soundCloudUsesNativePlayback)
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

        if (soundCloudWebView?.CoreWebView2 is null) return;
        if (soundCloudIsPlaying)
            await soundCloudWebView.CoreWebView2.ExecuteScriptAsync("window.scPause && window.scPause()");
        else
            await soundCloudWebView.CoreWebView2.ExecuteScriptAsync("window.scPlay && window.scPlay()");
    }

    internal async Task SoundCloudSeekAsync(float seconds)
    {
        if (soundCloudUsesNativePlayback)
        {
            engine.Seek(seconds);
            SyncNativeExternalPlaybackState();
            UpdateUiState();
            await Task.CompletedTask;
            return;
        }

        if (soundCloudWebView?.CoreWebView2 is null) return;
        var ms = (int)(seconds * 1000);
        await soundCloudWebView.CoreWebView2.ExecuteScriptAsync($"window.scSeek && window.scSeek({ms})");
        soundCloudPositionSeconds = seconds;
    }

    internal void OnSoundCloudNaturalEnd()
    {
        var hadNext = queue.HasNext;
        var shouldResumeSpotify = resumeSpotifyAfterLocalPlayback;

        if (hadNext || shouldResumeSpotify)
        {
            StopSoundCloudPlayback();
        }
        else
        {
            MarkSoundCloudEnded();
        }

        if (hadNext)
            NavigateNext();
        else if (shouldResumeSpotify)
            _ = ResumeSpotifyAfterLocalPlaybackAsync();

        UpdateUiState();
    }

    private void MarkSoundCloudEnded()
    {
        soundCloudIsPlaying = false;
        if (soundCloudDurationSeconds > 0)
            soundCloudPositionSeconds = soundCloudDurationSeconds;
        StopSoundCloudLoopback();
        soundCloudVisualizer?.Clear();
    }

    internal void StopSoundCloudPlayback()
    {
        soundCloudLoadCts?.Cancel();
        if (soundCloudUsesNativePlayback)
        {
            engine.Unload();
            RemoteAudioCache.TryDelete(soundCloudTempAudioPath);
            soundCloudTempAudioPath = null;
        }
        else if (soundCloudWebView?.CoreWebView2 is not null)
        {
            try { _ = soundCloudWebView.CoreWebView2.ExecuteScriptAsync("window.scPause && window.scPause()"); }
            catch { }
        }

        soundCloudArtCts?.Cancel();
        StopSoundCloudLoopback();
        soundCloudUsesNativePlayback = false;
        soundCloudCurrentTrack = null;
        soundCloudLoadId++;
        soundCloudIsPlaying = false;
        soundCloudPositionSeconds = 0;
        soundCloudDurationSeconds = 0;
        soundCloudVisualizer?.Clear();
    }

    private void StopSoundCloudLoopback()
    {
        soundCloudLoopback?.Stop();
        soundCloudLoopback?.Dispose();
        soundCloudLoopback = null;
    }

    private static LyricsDocument? BuildSoundCloudDescriptionLyrics(string? description, double durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(description))
            return null;

        var lines = description
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            .Split('\n')
            .Select(static l => l.Trim())
            .Where(static l => l.Length > 0)
            .Select(static (line, i) => new LyricsLine(i, line));

        return new LyricsDocument(lines, sourceLabel: "SoundCloud description", isDescription: true);
    }

    private void SetSoundCloudStatus(string message)
    {
        soundCloudStatusMessage = message;
        if (IsHandleCreated)
            BeginInvoke(UpdateUiState);
    }

    private void DisposeSoundCloud()
    {
        StopSoundCloudPlayback();
        soundCloudWebView?.Dispose();
        soundCloudWebView = null;
    }
}
