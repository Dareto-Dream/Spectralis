using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Spectralis;

public partial class Form1
{
    private const int WmClipboardUpdate = 0x031D;
    private const int ClipboardUrlHistoryLimit = 96;

    private static readonly Regex ClipboardUrlRegex = new(
        @"https?://[^\s<>'""]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly HttpClient ClipboardMetadataHttp = CreateClipboardMetadataHttp();

    private readonly HashSet<string> clipboardUrlHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> clipboardUrlHistoryOrder = new();

    private bool clipboardListenerRegistered;
    private IntPtr clipboardListenerHandle;
    private CancellationTokenSource? clipboardUrlExpansionCts;
    private ClipboardPromptSession? clipboardPromptSession;
    private ClipboardPromptPopup? clipboardPromptPopup;
    private CancellationTokenSource? clipboardPopupArtworkCts;
    private System.Windows.Forms.Timer? clipboardPopupDismissTimer;

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        if (m.Msg == WmClipboardUpdate)
            _ = HandleClipboardUpdateAsync();
    }

    private void InitializeClipboardMonitor()
    {
        HandleCreated += (_, _) => ApplyClipboardMonitorSettings();
        HandleDestroyed += (_, _) => UnregisterClipboardListener();
        Move += (_, _) => PlaceClipboardPopup();
        Resize += (_, _) => PlaceClipboardPopup();

        clipboardPopupDismissTimer = new System.Windows.Forms.Timer { Interval = 30000 };
        clipboardPopupDismissTimer.Tick += (_, _) => DismissClipboardPrompt();

        ApplyClipboardMonitorSettings();
    }

    private void ApplyClipboardMonitorSettings()
    {
        if (!IsHandleCreated)
            return;

        if (appSettings.EnableClipboardUrlMonitoring)
        {
            RegisterClipboardListener();
            return;
        }

        UnregisterClipboardListener();
        clipboardUrlExpansionCts?.Cancel();
        DismissClipboardPrompt();
    }

    private void DisposeClipboardMonitor()
    {
        UnregisterClipboardListener();
        clipboardUrlExpansionCts?.Cancel();
        clipboardUrlExpansionCts?.Dispose();
        clipboardUrlExpansionCts = null;
        ClearClipboardPromptSession();

        clipboardPopupArtworkCts?.Cancel();
        clipboardPopupArtworkCts?.Dispose();
        clipboardPopupArtworkCts = null;

        clipboardPopupDismissTimer?.Stop();
        clipboardPopupDismissTimer?.Dispose();
        clipboardPopupDismissTimer = null;

        clipboardPromptPopup?.Dispose();
        clipboardPromptPopup = null;
    }

    private void RegisterClipboardListener()
    {
        if (clipboardListenerRegistered)
            return;

        var hwnd = Handle;
        clipboardListenerRegistered = AddClipboardFormatListener(hwnd);
        clipboardListenerHandle = clipboardListenerRegistered ? hwnd : IntPtr.Zero;
    }

    private void UnregisterClipboardListener()
    {
        if (!clipboardListenerRegistered)
            return;

        RemoveClipboardFormatListener(clipboardListenerHandle);
        clipboardListenerRegistered = false;
        clipboardListenerHandle = IntPtr.Zero;
    }

    private async Task HandleClipboardUpdateAsync()
    {
        if (!appSettings.EnableClipboardUrlMonitoring || !IsHandleCreated)
            return;

        await Task.Delay(80);

        if (!appSettings.EnableClipboardUrlMonitoring || IsDisposed)
            return;

        var text = TryReadClipboardText();
        if (string.IsNullOrWhiteSpace(text))
            return;

        var urls = EnumerateClipboardUrls(text).ToArray();
        if (urls.Length == 0)
            return;

        var previousCts = clipboardUrlExpansionCts;
        previousCts?.Cancel();

        var cts = new CancellationTokenSource();
        clipboardUrlExpansionCts = cts;

        try
        {
            foreach (var url in urls)
            {
                ExpandedOpenUrl? expandedUrl;
                try
                {
                    expandedUrl = await TryExpandOpenUrlAsync(url, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (expandedUrl is null)
                    continue;

                var historyKey = BuildClipboardHistoryKey(expandedUrl.Url, expandedUrl.Target);
                if (clipboardUrlHistory.Contains(historyKey))
                    continue;

                var candidate = new ClipboardUrlCandidate(
                    expandedUrl.Url,
                    expandedUrl.Target,
                    historyKey);
                AddClipboardUrlHistory(candidate.HistoryKey);
                StartClipboardPromptPreparation(candidate);
                return;
            }
        }
        finally
        {
            if (ReferenceEquals(clipboardUrlExpansionCts, cts))
            {
                clipboardUrlExpansionCts = null;
            }

            cts.Dispose();
        }
    }

    private static string? TryReadClipboardText()
    {
        try
        {
            return Clipboard.ContainsText(TextDataFormat.UnicodeText)
                ? Clipboard.GetText(TextDataFormat.UnicodeText)
                : null;
        }
        catch (ExternalException)
        {
            return null;
        }
        catch (ThreadStateException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateClipboardUrls(string text)
    {
        foreach (Match match in ClipboardUrlRegex.Matches(text))
        {
            var url = TrimClipboardUrl(match.Value);
            if (!string.IsNullOrWhiteSpace(url))
                yield return url;
        }
    }

    private static string TrimClipboardUrl(string value)
    {
        var trimmed = value.Trim().Trim('<', '>', '\'', '"');
        return trimmed.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']');
    }

    private static string BuildClipboardHistoryKey(string url, OpenUrlTarget target)
    {
        if (target == OpenUrlTarget.YouTube && ExtractYouTubeVideoId(url) is { } videoId)
            return $"youtube:{videoId.ToLowerInvariant()}";

        if (target == OpenUrlTarget.Suno && SunoClipResolver.TryExtractClipId(url, out var clipId))
            return $"suno:{clipId}";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var builder = new UriBuilder(uri) { Fragment = "" };
            return $"{target}:{builder.Uri.AbsoluteUri.TrimEnd('/')}";
        }

        return $"{target}:{url.Trim()}";
    }

    private void AddClipboardUrlHistory(string historyKey)
    {
        if (!clipboardUrlHistory.Add(historyKey))
            return;

        clipboardUrlHistoryOrder.Enqueue(historyKey);
        while (clipboardUrlHistoryOrder.Count > ClipboardUrlHistoryLimit)
            clipboardUrlHistory.Remove(clipboardUrlHistoryOrder.Dequeue());
    }

    private void StartClipboardPromptPreparation(ClipboardUrlCandidate candidate)
    {
        ClearClipboardPromptSession();

        var session = new ClipboardPromptSession(candidate);
        clipboardPromptSession = session;
        session.PreparationTask = PrepareClipboardPromptAsync(session);
    }

    private async Task PrepareClipboardPromptAsync(ClipboardPromptSession session)
    {
        try
        {
            switch (session.Candidate.Target)
            {
                case OpenUrlTarget.Suno:
                    await PrepareSunoClipboardPromptAsync(session);
                    break;

                case OpenUrlTarget.SoundCloud:
                    await PrepareSoundCloudClipboardPromptAsync(session);
                    break;

                case OpenUrlTarget.YouTube:
                    await PrepareYouTubeClipboardPromptAsync(session);
                    break;

                case OpenUrlTarget.Untitled:
                    await PrepareUntitledClipboardPromptAsync(session);
                    break;

                case OpenUrlTarget.BandLab:
                    await PrepareBandLabClipboardPromptAsync(session);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (session.Cancellation.IsCancellationRequested)
                return;

            session.Track ??= CreateFallbackClipboardTrack(session.Candidate);
            session.PreparedPlayback?.Dispose();
            session.PreparedPlayback = ClipboardPreparedPlayback.StreamOnAccept();
            ShowOrUpdateClipboardPopup(session, "Ready to stream", isPreparing: false);
        }
    }

    private async Task PrepareSunoClipboardPromptAsync(ClipboardPromptSession session)
    {
        var cancellationToken = session.Cancellation.Token;
        var clip = await SunoClipResolver.ResolveAsync(session.Candidate.Url, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        session.Track = new ClipboardDetectedTrack(
            session.Candidate.Url,
            session.Candidate.Target,
            FirstNonEmpty(clip.Title, "Suno track")!,
            FirstNonEmpty(clip.Artist, "Suno"),
            "Suno",
            FirstNonEmpty(clip.ImageLargeUrl, clip.ImageUrl));
        ShowOrUpdateClipboardPopup(session, "Caching audio...", isPreparing: true);

        string? cachedPath = null;
        try
        {
            var artworkTask = FetchSunoArtworkAsync(clip, cancellationToken);
            cachedPath = await RemoteAudioCache.DownloadAsync(clip.AudioUrl, ".mp3", cancellationToken);
            var artBytes = await artworkTask;
            cancellationToken.ThrowIfCancellationRequested();

            var durationSeconds = Math.Max(0, clip.DurationSeconds ?? 0);
            var lyrics = SunoLyricsBuilder.BuildDescription(clip.LyricsText);
            var album = string.IsNullOrWhiteSpace(clip.Tags) ? "Suno" : clip.Tags;
            var trackInfo = new AudioTrackInfo(
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
                TimeSpan.FromSeconds(durationSeconds));

            var preparedEngine = CreateClipboardPreparedEngine(cachedPath, trackInfo);
            session.PreparedPlayback = ClipboardPreparedPlayback.NativeSuno(
                cachedPath,
                preparedEngine,
                trackInfo,
                clip);
            cachedPath = null;

            ShowOrUpdateClipboardPopup(session, "Ready", isPreparing: false);
        }
        catch
        {
            RemoteAudioCache.TryDelete(cachedPath);
            throw;
        }
    }

    private async Task PrepareSoundCloudClipboardPromptAsync(ClipboardPromptSession session)
    {
        var cancellationToken = session.Cancellation.Token;
        var metadataTask = ResolveOEmbedClipboardTrackAsync(
            session.Candidate,
            "https://soundcloud.com/oembed?format=json&url=",
            "SoundCloud",
            "SoundCloud track",
            cancellationToken);

        session.Track = await metadataTask;
        cancellationToken.ThrowIfCancellationRequested();
        ShowOrUpdateClipboardPopup(session, "Caching audio...", isPreparing: true);

        var playbackSource = await ResolveSoundCloudPlaybackSourceAsync(session.Candidate.Url.Trim());
        cancellationToken.ThrowIfCancellationRequested();

        var nativeTrack = await ResolveSoundCloudNativeTrackAsync(playbackSource, cancellationToken);
        if (nativeTrack is null)
        {
            await PrewarmSoundCloudPlayerAsync();
            cancellationToken.ThrowIfCancellationRequested();
            session.PreparedPlayback = ClipboardPreparedPlayback.StreamOnAccept();
            ShowOrUpdateClipboardPopup(session, "Ready to stream", isPreparing: false);
            return;
        }

        session.Track = new ClipboardDetectedTrack(
            session.Candidate.Url,
            session.Candidate.Target,
            FirstNonEmpty(nativeTrack.Title, session.Track.Title, "SoundCloud track")!,
            FirstNonEmpty(nativeTrack.Artist, session.Track.Artist),
            "SoundCloud",
            FirstNonEmpty(nativeTrack.ArtworkUrl, session.Track.ArtworkUrl));
        ShowOrUpdateClipboardPopup(session, "Caching audio...", isPreparing: true);

        string? cachedPath = null;
        try
        {
            var artworkTask = FetchSoundCloudArtworkBytesAsync(nativeTrack.ArtworkUrl, cancellationToken);
            cachedPath = await RemoteAudioCache.DownloadAsync(nativeTrack.StreamUrl, ".mp3", cancellationToken);
            var artBytes = await artworkTask;
            cancellationToken.ThrowIfCancellationRequested();

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

            var preparedEngine = CreateClipboardPreparedEngine(cachedPath, trackInfo);
            session.PreparedPlayback = ClipboardPreparedPlayback.NativeSoundCloud(
                cachedPath,
                preparedEngine,
                trackInfo,
                nativeTrack);
            cachedPath = null;

            ShowOrUpdateClipboardPopup(session, "Ready", isPreparing: false);
        }
        catch
        {
            RemoteAudioCache.TryDelete(cachedPath);
            throw;
        }
    }

    private async Task PrepareYouTubeClipboardPromptAsync(ClipboardPromptSession session)
    {
        var cancellationToken = session.Cancellation.Token;
        session.Track = await ResolveOEmbedClipboardTrackAsync(
            session.Candidate,
            "https://www.youtube.com/oembed?format=json&url=",
            "YouTube",
            "YouTube video",
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        ShowOrUpdateClipboardPopup(session, "Preparing player...", isPreparing: true);

        await PrewarmYouTubePlayerAsync();
        cancellationToken.ThrowIfCancellationRequested();

        session.PreparedPlayback = ClipboardPreparedPlayback.StreamOnAccept();
        ShowOrUpdateClipboardPopup(session, "Ready to stream", isPreparing: false);
    }

    private AudioEngine CreateClipboardPreparedEngine(string path, AudioTrackInfo trackInfo)
    {
        var preparedEngine = new AudioEngine();
        try
        {
            preparedEngine.SetMidiPlaybackInstrument(appSettings.MidiInstrument);
            preparedEngine.SetPreferredSampleRate(appSettings.PreferredSampleRate);
            preparedEngine.Volume = trackBarVolume.Value / 100f;
            preparedEngine.Load(path, trackInfo);
            return preparedEngine;
        }
        catch
        {
            preparedEngine.Dispose();
            throw;
        }
    }

    private async Task PrewarmSoundCloudPlayerAsync()
    {
        if (soundCloudWebView is not null)
            return;

        if (soundCloudPrewarmTask is not null)
        {
            try { await soundCloudPrewarmTask; }
            catch { }
            soundCloudPrewarmTask = null;
        }

        if (soundCloudWebView is null)
            await InitializeSoundCloudAsync();
    }

    private Task PrewarmYouTubePlayerAsync() => Task.CompletedTask;

    private void ShowOrUpdateClipboardPopup(
        ClipboardPromptSession session,
        string statusText,
        bool isPreparing)
    {
        if (!ReferenceEquals(clipboardPromptSession, session) ||
            session.Track is null ||
            IsDisposed)
        {
            return;
        }

        EnsureClipboardPopupCreated();
        if (clipboardPromptPopup is null)
            return;

        clipboardPromptPopup.UpdateTrack(session.Track, statusText, isPreparing);
        ApplyClipboardPopupTheme();
        PlaceClipboardPopup();

        if (!clipboardPromptPopup.Visible)
            clipboardPromptPopup.Show(this);

        clipboardPromptPopup.BringToFront();
        LoadClipboardPopupArtwork(session.Track);

        clipboardPopupDismissTimer?.Stop();
        clipboardPopupDismissTimer?.Start();
    }

    private void EnsureClipboardPopupCreated()
    {
        if (clipboardPromptPopup is not null && !clipboardPromptPopup.IsDisposed)
            return;

        clipboardPromptPopup = new ClipboardPromptPopup();
        clipboardPromptPopup.PlayRequested += (_, _) => PlayClipboardPrompt();
        clipboardPromptPopup.DismissRequested += (_, _) => DismissClipboardPrompt();
        ApplyClipboardPopupTheme();
    }

    private void PlaceClipboardPopup()
    {
        if (clipboardPromptPopup is null || clipboardPromptPopup.IsDisposed)
            return;

        var clientPoint = new Point(
            Math.Max(12, ClientSize.Width - clipboardPromptPopup.Width - 24),
            Math.Max(12, menuStrip1.Bottom + 14));
        clipboardPromptPopup.Location = PointToScreen(clientPoint);
    }

    private void ApplyClipboardPopupTheme()
    {
        if (clipboardPromptPopup is null || clipboardPromptPopup.IsDisposed)
            return;

        clipboardPromptPopup.ApplyTheme(themePalette);
    }

    private async void LoadClipboardPopupArtwork(ClipboardDetectedTrack track)
    {
        clipboardPopupArtworkCts?.Cancel();
        clipboardPopupArtworkCts?.Dispose();
        clipboardPopupArtworkCts = new CancellationTokenSource();
        var cancellationToken = clipboardPopupArtworkCts.Token;

        clipboardPromptPopup?.ClearArtwork();

        if (string.IsNullOrWhiteSpace(track.ArtworkUrl))
            return;

        try
        {
            var bytes = await ClipboardMetadataHttp.GetByteArrayAsync(track.ArtworkUrl, cancellationToken);
            using var stream = new MemoryStream(bytes);
            using var image = Image.FromStream(stream);
            var clone = (Image)image.Clone();

            if (cancellationToken.IsCancellationRequested ||
                clipboardPromptPopup is null ||
                clipboardPromptPopup.IsDisposed)
            {
                clone.Dispose();
                return;
            }

            clipboardPromptPopup.SetArtwork(clone);
        }
        catch
        {
            // Artwork is nice to have; the popup still has track metadata.
        }
    }

    private async void PlayClipboardPrompt()
    {
        var session = clipboardPromptSession;
        if (session is null)
            return;

        clipboardPopupDismissTimer?.Stop();
        clipboardPromptPopup?.SetStatus("Starting...", isPreparing: true);

        try
        {
            if (session.PreparationTask is not null)
                await session.PreparationTask;

            if (!ReferenceEquals(clipboardPromptSession, session))
                return;

            var playback = session.PreparedPlayback;
            session.PreparedPlayback = null;
            clipboardPromptSession = null;
            clipboardPromptPopup?.Hide();

            await ActivateClipboardPreparedPlaybackAsync(session.Candidate, playback);
            session.Dispose();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(clipboardPromptSession, session))
                clipboardPromptSession = null;

            clipboardPromptPopup?.Hide();
            session.Dispose();
            ShowError(
                $"Spectralis could not play the copied URL.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Clipboard URL");
        }
    }

    private async Task ActivateClipboardPreparedPlaybackAsync(
        ClipboardUrlCandidate candidate,
        ClipboardPreparedPlayback? playback)
    {
        if (playback?.Kind is not ClipboardPreparedPlaybackKind.NativeSuno and
            not ClipboardPreparedPlaybackKind.NativeSoundCloud)
        {
            playback?.Dispose();
            await OpenDetectedUrlAsync(candidate.Url);
            return;
        }

        var preparedEngine = playback.ReleasePreparedEngine();
        var cachedPath = playback.ReleaseCachedAudioPath();
        if (preparedEngine is null || string.IsNullOrWhiteSpace(cachedPath))
        {
            playback.Dispose();
            await OpenDetectedUrlAsync(candidate.Url);
            return;
        }

        try
        {
            if (IsSpotifyActive || spotifyService.IsLinked)
                ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: false, advanceOnResume: false);
            if (IsYouTubeActive)
                StopYouTubePlayback();
            if (IsSoundCloudActive)
                StopSoundCloudPlayback();
            if (IsSunoActive)
                StopSunoPlayback();

            engine.Volume = trackBarVolume.Value / 100f;
            preparedEngine.Volume = trackBarVolume.Value / 100f;
            engine.TakePlaybackFrom(preparedEngine);
            preparedEngine.Dispose();
            preparedEngine = null;

            switch (playback.Kind)
            {
                case ClipboardPreparedPlaybackKind.NativeSoundCloud:
                    ActivatePreparedSoundCloudPlayback(playback, cachedPath);
                    break;

                case ClipboardPreparedPlaybackKind.NativeSuno:
                    ActivatePreparedSunoPlayback(playback, cachedPath);
                    break;
            }

            engine.Play();
            SyncNativeExternalPlaybackState();
            UpdateAlbumArt(engine.CurrentTrack);
            UpdateUiState();
        }
        catch
        {
            preparedEngine?.Dispose();
            RemoteAudioCache.TryDelete(cachedPath);
            throw;
        }
        finally
        {
            playback.Dispose();
        }
    }

    private void ActivatePreparedSoundCloudPlayback(
        ClipboardPreparedPlayback playback,
        string cachedPath)
    {
        soundCloudLoadId++;
        soundCloudTempAudioPath = cachedPath;
        soundCloudUsesNativePlayback = true;
        soundCloudCurrentTrack = engine.CurrentTrack ?? playback.TrackInfo;
        soundCloudPositionSeconds = 0;
        soundCloudDurationSeconds = engine.GetLength() > 0
            ? engine.GetLength()
            : (float)playback.TrackInfo.Duration.TotalSeconds;
        soundCloudStatusMessage = null;
        soundCloudIsPlaying = false;
    }

    private void ActivatePreparedSunoPlayback(
        ClipboardPreparedPlayback playback,
        string cachedPath)
    {
        sunoLoadId++;
        sunoTempAudioPath = cachedPath;
        sunoUsesNativePlayback = true;
        sunoCurrentClip = playback.SunoClip;
        sunoCurrentTrack = engine.CurrentTrack ?? playback.TrackInfo;
        sunoPositionSeconds = 0;
        sunoDurationSeconds = engine.GetLength() > 0
            ? engine.GetLength()
            : (float)playback.TrackInfo.Duration.TotalSeconds;
        sunoStatusMessage = null;
        sunoIsPlaying = false;
    }

    private void DismissClipboardPrompt()
    {
        clipboardPopupDismissTimer?.Stop();
        clipboardPopupArtworkCts?.Cancel();
        clipboardPromptPopup?.ClearArtwork();
        clipboardPromptPopup?.Hide();
        ClearClipboardPromptSession();
    }

    private void ClearClipboardPromptSession()
    {
        clipboardPromptSession?.Dispose();
        clipboardPromptSession = null;
    }

    private static async Task<ClipboardDetectedTrack> ResolveOEmbedClipboardTrackAsync(
        ClipboardUrlCandidate candidate,
        string endpointPrefix,
        string sourceLabel,
        string fallbackTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            var json = await ClipboardMetadataHttp.GetStringAsync(
                endpointPrefix + Uri.EscapeDataString(candidate.Url),
                cancellationToken);
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var title = FirstNonEmpty(TryGetClipboardJsonString(root, "title"), fallbackTitle)!;
            var artist = TryGetClipboardJsonString(root, "author_name");
            var thumbnailUrl = TryGetClipboardJsonString(root, "thumbnail_url");

            return new ClipboardDetectedTrack(
                candidate.Url,
                candidate.Target,
                title,
                artist,
                sourceLabel,
                thumbnailUrl);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return await ResolveOpenGraphClipboardTrackAsync(
                candidate,
                sourceLabel,
                fallbackTitle,
                cancellationToken);
        }
    }

    private static async Task<ClipboardDetectedTrack> ResolveOpenGraphClipboardTrackAsync(
        ClipboardUrlCandidate candidate,
        string sourceLabel,
        string fallbackTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await ClipboardMetadataHttp.GetStringAsync(candidate.Url, cancellationToken);
            var title = FirstNonEmpty(
                ExtractMetaContent(html, "og:title"),
                ExtractMetaContent(html, "twitter:title"),
                ExtractHtmlTitle(html),
                fallbackTitle)!;
            var artist = FirstNonEmpty(
                ExtractMetaContent(html, "music:musician"),
                ExtractMetaContent(html, "twitter:creator"),
                ExtractMetaContent(html, "author"));
            var image = FirstNonEmpty(
                ExtractMetaContent(html, "og:image"),
                ExtractMetaContent(html, "twitter:image"));

            return new ClipboardDetectedTrack(
                candidate.Url,
                candidate.Target,
                title,
                artist,
                sourceLabel,
                image);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return CreateFallbackClipboardTrack(candidate);
        }
    }

    private static ClipboardDetectedTrack CreateFallbackClipboardTrack(ClipboardUrlCandidate candidate)
    {
        var source = candidate.Target switch
        {
            OpenUrlTarget.YouTube => "YouTube",
            OpenUrlTarget.SoundCloud => "SoundCloud",
            OpenUrlTarget.Suno => "Suno",
            OpenUrlTarget.Untitled => "Untitled",
            OpenUrlTarget.BandLab => "BandLab",
            _ => "URL"
        };

        return new ClipboardDetectedTrack(
            candidate.Url,
            candidate.Target,
            $"{source} track",
            null,
            source,
            null);
    }

    private static string? TryGetClipboardJsonString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static string? ExtractMetaContent(string html, string name)
    {
        foreach (Match match in Regex.Matches(html, "<meta\\s+[^>]*>", RegexOptions.IgnoreCase))
        {
            var tag = match.Value;
            var content = ReadHtmlAttribute(tag, "content");
            if (string.IsNullOrWhiteSpace(content))
                continue;

            var property = ReadHtmlAttribute(tag, "property");
            var metaName = ReadHtmlAttribute(tag, "name");
            if (string.Equals(property, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(metaName, name, StringComparison.OrdinalIgnoreCase))
            {
                return CleanMetadataText(content);
            }
        }

        return null;
    }

    private static string? ExtractHtmlTitle(string html)
    {
        var match = Regex.Match(
            html,
            @"<title[^>]*>(?<title>.*?)</title>",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? CleanMetadataText(match.Groups["title"].Value) : null;
    }

    private static string? ReadHtmlAttribute(string tag, string attributeName)
    {
        var match = Regex.Match(
            tag,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups["value"].Value : null;
    }

    private static string? CleanMetadataText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var decoded = WebUtility.HtmlDecode(value).Trim();
        return string.IsNullOrWhiteSpace(decoded) ? null : decoded;
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            var clean = CleanMetadataText(value);
            if (!string.IsNullOrWhiteSpace(clean))
                return clean;
        }

        return null;
    }

    private static HttpClient CreateClipboardMetadataHttp()
    {
        var http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(12)
        };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/json,text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
        return http;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    private sealed record ClipboardUrlCandidate(
        string Url,
        OpenUrlTarget Target,
        string HistoryKey);

    private sealed record ClipboardDetectedTrack(
        string Url,
        OpenUrlTarget Target,
        string Title,
        string? Artist,
        string SourceLabel,
        string? ArtworkUrl);

    private sealed class ClipboardPromptSession : IDisposable
    {
        private bool isDisposed;

        public ClipboardPromptSession(ClipboardUrlCandidate candidate)
        {
            Candidate = candidate;
        }

        public ClipboardUrlCandidate Candidate { get; }
        public CancellationTokenSource Cancellation { get; } = new();
        public ClipboardDetectedTrack? Track { get; set; }
        public ClipboardPreparedPlayback? PreparedPlayback { get; set; }
        public Task? PreparationTask { get; set; }

        public void Dispose()
        {
            if (isDisposed)
                return;

            isDisposed = true;
            Cancellation.Cancel();
            PreparedPlayback?.Dispose();
            PreparedPlayback = null;

            if (PreparationTask is null || PreparationTask.IsCompleted)
            {
                Cancellation.Dispose();
            }
            else
            {
                _ = PreparationTask.ContinueWith(
                    _ => Cancellation.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }

    private enum ClipboardPreparedPlaybackKind
    {
        StreamOnAccept,
        NativeSoundCloud,
        NativeSuno
    }

    private sealed class ClipboardPreparedPlayback : IDisposable
    {
        private string? cachedAudioPath;
        private AudioEngine? preparedEngine;

        private ClipboardPreparedPlayback(
            ClipboardPreparedPlaybackKind kind,
            string? cachedAudioPath,
            AudioEngine? preparedEngine,
            AudioTrackInfo? trackInfo,
            SunoClipInfo? sunoClip)
        {
            Kind = kind;
            this.cachedAudioPath = cachedAudioPath;
            this.preparedEngine = preparedEngine;
            TrackInfo = trackInfo ?? new AudioTrackInfo(
                "",
                "Clipboard track",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                "",
                2,
                44100,
                16,
                TimeSpan.Zero);
            SunoClip = sunoClip;
        }

        public ClipboardPreparedPlaybackKind Kind { get; }
        public AudioTrackInfo TrackInfo { get; }
        public SunoClipInfo? SunoClip { get; }

        public static ClipboardPreparedPlayback StreamOnAccept() =>
            new(ClipboardPreparedPlaybackKind.StreamOnAccept, null, null, null, null);

        public static ClipboardPreparedPlayback NativeSoundCloud(
            string cachedAudioPath,
            AudioEngine preparedEngine,
            AudioTrackInfo trackInfo,
            SoundCloudNativeTrack nativeTrack) =>
            new(ClipboardPreparedPlaybackKind.NativeSoundCloud, cachedAudioPath, preparedEngine, trackInfo, null);

        public static ClipboardPreparedPlayback NativeSuno(
            string cachedAudioPath,
            AudioEngine preparedEngine,
            AudioTrackInfo trackInfo,
            SunoClipInfo clip) =>
            new(ClipboardPreparedPlaybackKind.NativeSuno, cachedAudioPath, preparedEngine, trackInfo, clip);

        public AudioEngine? ReleasePreparedEngine()
        {
            var result = preparedEngine;
            preparedEngine = null;
            return result;
        }

        public string? ReleaseCachedAudioPath()
        {
            var result = cachedAudioPath;
            cachedAudioPath = null;
            return result;
        }

        public void Dispose()
        {
            preparedEngine?.Dispose();
            preparedEngine = null;
            RemoteAudioCache.TryDelete(cachedAudioPath);
            cachedAudioPath = null;
        }
    }

    private sealed class ClipboardPromptPopup : Form
    {
        private readonly PopupSurfacePanel surface;
        private readonly PictureBox artwork;
        private readonly Label headingLabel;
        private readonly Label titleLabel;
        private readonly Label metaLabel;
        private readonly Label statusLabel;
        private readonly ModernButton playButton;
        private readonly ModernButton dismissButton;

        public ClipboardPromptPopup()
        {
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(430, 150);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            Padding = Padding.Empty;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "Copied URL";

            surface = new PopupSurfacePanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(13)
            };

            var layout = new TableLayoutPanel
            {
                BackColor = Color.Transparent,
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                RowCount = 1
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 78F));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            artwork = new PictureBox
            {
                Dock = DockStyle.Top,
                Margin = new Padding(0, 3, 14, 0),
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom
            };

            var textStack = new TableLayoutPanel
            {
                BackColor = Color.Transparent,
                ColumnCount = 1,
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                RowCount = 5
            };
            textStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 18F));
            textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 27F));
            textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 21F));
            textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            textStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            headingLabel = CreatePopupLabel(
                "Play copied track?",
                new Font("Segoe UI Semibold", 8F, FontStyle.Bold, GraphicsUnit.Point));
            titleLabel = CreatePopupLabel(
                "",
                new Font("Segoe UI Semibold", 10.25F, FontStyle.Bold, GraphicsUnit.Point));
            metaLabel = CreatePopupLabel(
                "",
                new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point));
            statusLabel = CreatePopupLabel(
                "",
                new Font("Segoe UI", 8.25F, FontStyle.Regular, GraphicsUnit.Point));

            var buttons = new FlowLayoutPanel
            {
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 7, 0, 0),
                Padding = Padding.Empty,
                WrapContents = false
            };

            playButton = new ModernButton
            {
                Margin = new Padding(0, 0, 8, 0),
                Size = new Size(86, 30),
                Text = "Play"
            };
            playButton.Click += (_, _) => PlayRequested?.Invoke(this, EventArgs.Empty);

            dismissButton = new ModernButton
            {
                Margin = Padding.Empty,
                Size = new Size(86, 30),
                Text = "No"
            };
            dismissButton.Click += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);

            buttons.Controls.Add(playButton);
            buttons.Controls.Add(dismissButton);

            textStack.Controls.Add(headingLabel, 0, 0);
            textStack.Controls.Add(titleLabel, 0, 1);
            textStack.Controls.Add(metaLabel, 0, 2);
            textStack.Controls.Add(statusLabel, 0, 3);
            textStack.Controls.Add(buttons, 0, 4);

            layout.Controls.Add(artwork, 0, 0);
            layout.Controls.Add(textStack, 1, 0);
            surface.Controls.Add(layout);
            Controls.Add(surface);
        }

        public event EventHandler? PlayRequested;
        public event EventHandler? DismissRequested;

        public void UpdateTrack(ClipboardDetectedTrack track, string statusText, bool isPreparing)
        {
            headingLabel.Text = "Play copied track?";
            titleLabel.Text = track.Title;
            metaLabel.Text = string.IsNullOrWhiteSpace(track.Artist)
                ? track.SourceLabel
                : $"{track.Artist} - {track.SourceLabel}";
            SetStatus(statusText, isPreparing);
        }

        public void SetStatus(string statusText, bool isPreparing)
        {
            statusLabel.Text = statusText;
            playButton.Text = isPreparing ? "Play" : "Play";
            playButton.Enabled = true;
        }

        public void ApplyTheme(ThemePalette palette)
        {
            BackColor = palette.SurfaceRaisedColor;
            surface.BackColor = palette.SurfaceRaisedColor;
            surface.BorderColor = palette.BorderStrongColor;
            artwork.BackColor = palette.SurfaceBackColor;
            headingLabel.ForeColor = palette.AccentPrimaryColor;
            titleLabel.ForeColor = palette.TextPrimaryColor;
            metaLabel.ForeColor = palette.TextSecondaryColor;
            statusLabel.ForeColor = palette.TextMutedColor;
            ThemeControlStyler.ApplyPrimaryButtonTheme(playButton, palette, palette.AccentPrimaryColor);
            ThemeControlStyler.ApplyGhostButtonTheme(dismissButton, palette, palette.AccentSoftColor);
            surface.Invalidate();
        }

        public void SetArtwork(Image image)
        {
            ClearArtwork();
            artwork.Image = image;
        }

        public void ClearArtwork()
        {
            if (artwork.Image is null)
                return;

            var image = artwork.Image;
            artwork.Image = null;
            image.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                ClearArtwork();

            base.Dispose(disposing);
        }

        private static Label CreatePopupLabel(string text, Font font) =>
            new()
            {
                AutoEllipsis = true,
                AutoSize = false,
                BackColor = Color.Transparent,
                Dock = DockStyle.Fill,
                Font = font,
                Margin = Padding.Empty,
                Text = text,
                TextAlign = ContentAlignment.MiddleLeft
            };
    }

    private sealed class PopupSurfacePanel : Panel
    {
        private Color borderColor;

        public PopupSurfacePanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.UserPaint |
                ControlStyles.ResizeRedraw,
                true);
        }

        public Color BorderColor
        {
            get => borderColor;
            set
            {
                borderColor = value;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return;

            using var path = CreateRoundedPath(bounds, 8f);
            using var fillBrush = new SolidBrush(BackColor);
            using var borderPen = new Pen(borderColor, 1f);
            e.Graphics.FillPath(fillBrush, path);
            e.Graphics.DrawPath(borderPen, path);
        }

        private static GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2f;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
