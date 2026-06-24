namespace Spectralis;

public partial class Form1
{
    private static readonly HttpClient OpenUrlExpansionHttp = CreateOpenUrlExpansionHttp();

    private enum OpenUrlTarget
    {
        YouTube,
        SoundCloud,
        Suno,
        Spotify,
        Untitled,
        BandLab,
        DirectAudio
    }

    private sealed record ExpandedOpenUrl(string Url, OpenUrlTarget Target);

    private void InitializeOpenUrlMenu()
    {
        var item = new ToolStripMenuItem
        {
            Name = "fileOpenUrlToolStripMenuItem",
            ShortcutKeys = Keys.Control | Keys.L,
            Text = "Open &URL..."
        };
        item.Click += (_, _) => ShowOpenUrlDialog();

        fileToolStripMenuItem.DropDownItems.Insert(1, item);
    }

    private void ShowOpenUrlDialog()
    {
        using var dialog = new OpenUrlDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        var input = dialog.Url;
        if (!string.IsNullOrWhiteSpace(input))
            _ = OpenDetectedUrlAsync(input);
    }

    private async Task OpenDetectedUrlAsync(string input)
    {
        var trimmed = input.Trim();
        try
        {
            var expandedUrl = await TryExpandOpenUrlAsync(trimmed, CancellationToken.None);
            if (expandedUrl is null)
            {
                ShowError(
                    "Paste a supported URL: YouTube, SoundCloud, Suno, Spotify, Untitled, BandLab, a Suno clip ID, or a direct audio file link.",
                    "Open URL");
                return;
            }

            switch (expandedUrl.Target)
            {
                case OpenUrlTarget.YouTube:
                    await LoadYouTubeUrlAsync(expandedUrl.Url);
                    return;

                case OpenUrlTarget.SoundCloud:
                    await LoadSoundCloudUrlAsync(expandedUrl.Url);
                    return;

                case OpenUrlTarget.Suno:
                    await LoadSunoUrlAsync(expandedUrl.Url);
                    return;

                case OpenUrlTarget.Spotify:
                    await LoadSpotifyUriAsync(NormalizeSpotifyPlaybackUri(expandedUrl.Url) ?? expandedUrl.Url);
                    return;

                case OpenUrlTarget.Untitled:
                    await LoadUntitledUrlAsync(expandedUrl.Url);
                    return;

                case OpenUrlTarget.BandLab:
                    await LoadBandLabUrlAsync(expandedUrl.Url);
                    return;

                case OpenUrlTarget.DirectAudio:
                    await LoadRemoteAudioUrlAsync(expandedUrl.Url);
                    return;
            }
        }
        catch (Exception ex)
        {
            ShowError(
                $"Spectralis could not open this URL.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open URL");
        }
    }

    private static async Task<ExpandedOpenUrl?> TryExpandOpenUrlAsync(
        string input,
        CancellationToken cancellationToken)
    {
        var trimmed = input.Trim();
        var initialTarget = DetectOpenUrlTarget(trimmed);
        if (!IsHttpUrl(trimmed))
        {
            return initialTarget is { } target
                ? new ExpandedOpenUrl(trimmed, target)
                : null;
        }

        string expandedUrl;
        try
        {
            expandedUrl = await ExpandOpenUrlRedirectsAsync(trimmed, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            expandedUrl = trimmed;
        }

        var expandedTarget = DetectOpenUrlTarget(expandedUrl);
        if (expandedTarget is not null)
            return new ExpandedOpenUrl(expandedUrl, expandedTarget.Value);

        return initialTarget is { } fallbackTarget
            ? new ExpandedOpenUrl(trimmed, fallbackTarget)
            : null;
    }

    private static async Task<string> ExpandOpenUrlRedirectsAsync(
        string url,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var currentUri) ||
            currentUri.Scheme is not ("http" or "https"))
        {
            return url;
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var redirect = 0; redirect < 8; redirect++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentUrl = currentUri.AbsoluteUri;
            if (!visited.Add(currentUrl))
                break;

            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            using var response = await OpenUrlExpansionHttp.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

            if (!IsOpenUrlRedirectStatusCode(response.StatusCode) ||
                response.Headers.Location is null)
            {
                break;
            }

            var nextUri = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(currentUri, response.Headers.Location);

            if (nextUri.Scheme is not ("http" or "https"))
                break;

            currentUri = nextUri;
        }

        return currentUri.AbsoluteUri;
    }

    private static bool IsHttpUrl(string input) =>
        Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https";

    private static bool IsOpenUrlRedirectStatusCode(System.Net.HttpStatusCode statusCode)
    {
        var code = (int)statusCode;
        return code is >= 300 and <= 399;
    }

    private static HttpClient CreateOpenUrlExpansionHttp()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false
        };
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/json;q=0.9,*/*;q=0.8");
        return http;
    }

    private static OpenUrlTarget? DetectOpenUrlTarget(string input)
    {
        if (IsYouTubeInput(input))
            return OpenUrlTarget.YouTube;

        if (IsSoundCloudInput(input))
            return OpenUrlTarget.SoundCloud;

        if (IsSpotifyInput(input))
            return OpenUrlTarget.Spotify;

        if (IsUntitledInput(input))
            return OpenUrlTarget.Untitled;

        if (IsBandLabInput(input))
            return OpenUrlTarget.BandLab;

        if (IsSunoInput(input))
            return OpenUrlTarget.Suno;

        if (IsDirectAudioUrl(input))
            return OpenUrlTarget.DirectAudio;

        return null;
    }

    private static bool IsYouTubeInput(string input)
    {
        if (ExtractYouTubeVideoId(input) is not null)
            return true;

        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            IsYouTubeHost(uri.Host);
    }

    private static bool IsYouTubeHost(string host) =>
        host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("youtube-nocookie.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".youtube-nocookie.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsSoundCloudInput(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && IsSoundCloudHost(uri))
            return true;

        return input.Contains("soundcloud.com/", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("on.soundcloud.com/", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("snd.sc/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSunoInput(string input)
    {
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
            return uri.Scheme is "http" or "https" && IsSunoHost(uri.Host);

        return input.Contains("suno.com/", StringComparison.OrdinalIgnoreCase) ||
            input.Contains("suno.ai/", StringComparison.OrdinalIgnoreCase) ||
            SunoClipResolver.TryExtractClipId(input, out _);
    }

    private static bool IsSunoHost(string host) =>
        host.Equals("suno.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".suno.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("suno.ai", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".suno.ai", StringComparison.OrdinalIgnoreCase);

    private static bool IsSpotifyInput(string input)
    {
        if (NormalizeSpotifyPlaybackUri(input) is not null)
            return true;

        return Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
            IsSpotifyHost(uri.Host);
    }

    private static bool IsSpotifyHost(string host) =>
        host.Equals("open.spotify.com", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".open.spotify.com", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("spotify.link", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".spotify.link", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeSpotifyPlaybackUri(string input)
    {
        var trimmed = input.Trim();
        if (trimmed.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
        {
            var parts = trimmed.Split(':', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 3 && IsSupportedSpotifyUriType(parts[1])
                ? $"spotify:{parts[1].ToLowerInvariant()}:{parts[2]}"
                : null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            !IsSpotifyHost(uri.Host))
        {
            return null;
        }

        var pathParts = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();

        for (var index = 0; index + 1 < pathParts.Length; index++)
        {
            var type = pathParts[index].ToLowerInvariant();
            if (IsSupportedSpotifyUriType(type))
                return $"spotify:{type}:{pathParts[index + 1]}";
        }

        return null;
    }

    private static bool IsSupportedSpotifyUriType(string type) =>
        type.Equals("track", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("album", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("playlist", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("artist", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("episode", StringComparison.OrdinalIgnoreCase);

    private static bool IsDirectAudioUrl(string input) =>
        Uri.TryCreate(input, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        SupportedAudioFormats.IsSupportedExtension(uri.AbsolutePath);

    private string? remoteAudioTempPath;
    private CancellationTokenSource? remoteAudioLoadCts;
    private int remoteAudioLoadId;

    private async Task LoadRemoteAudioUrlAsync(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https") ||
            !SupportedAudioFormats.IsSupportedExtension(uri.AbsolutePath))
        {
            ShowError("This direct audio link is not a supported format.", "Open URL");
            return;
        }

        StopRemoteAudioPlayback();
        var loadId = ++remoteAudioLoadId;
        var cts = remoteAudioLoadCts = new CancellationTokenSource();
        string? cachedPath = null;

        try
        {
            if (IsYouTubeActive)
                StopYouTubePlayback();
            if (IsSoundCloudActive)
                StopSoundCloudPlayback();
            if (IsSunoActive)
                StopSunoPlayback();
            if (IsSpotifyActive || spotifyService.IsLinked)
                ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);

            StopLocalPlaybackForExternalUrl();
            if (engine.IsLoaded)
                engine.Unload();
            UpdateUiState();

            var extension = Path.GetExtension(uri.AbsolutePath);
            cachedPath = await RemoteAudioCache.DownloadAsync(uri.AbsoluteUri, extension, cts.Token);
            if (cts.IsCancellationRequested || loadId != remoteAudioLoadId)
            {
                RemoteAudioCache.TryDelete(cachedPath);
                return;
            }

            var metadata = AudioMetadataReader.Read(cachedPath);
            var fileName = Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(uri.AbsolutePath));
            var trackInfo = new AudioTrackInfo(
                uri.AbsoluteUri,
                FirstNonEmptyOpenUrl(metadata.Title, fileName, "Remote audio"),
                metadata.Artist,
                metadata.Album,
                metadata.AlbumArtBytes,
                metadata.Lyrics,
                metadata.EmbeddedVisualizer,
                metadata.EmbeddedTheme,
                metadata.EmbeddedHtml,
                metadata.EmbeddedMarkdown,
                metadata.EmbeddedVideo,
                "Remote audio",
                2,
                44100,
                16,
                TimeSpan.Zero);

            engine.Load(cachedPath, trackInfo);
            engine.Volume = trackBarVolume.Value / 100f;
            remoteAudioTempPath = cachedPath;
            cachedPath = null;
            engine.Play();
            UpdateAlbumArt(engine.CurrentTrack);
            UpdateUiState();
        }
        catch (OperationCanceledException)
        {
            RemoteAudioCache.TryDelete(cachedPath);
        }
        catch (Exception ex)
        {
            RemoteAudioCache.TryDelete(cachedPath);
            ShowError(
                $"Spectralis could not open this audio link.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open URL");
        }
    }

    private void StopRemoteAudioPlayback()
    {
        remoteAudioLoadCts?.Cancel();
        if (string.IsNullOrWhiteSpace(remoteAudioTempPath))
            return;

        if (engine.IsLoaded)
            engine.Unload();

        RemoteAudioCache.TryDelete(remoteAudioTempPath);
        remoteAudioTempPath = null;
    }

    private static string FirstNonEmptyOpenUrl(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "Remote audio";
    }
}
