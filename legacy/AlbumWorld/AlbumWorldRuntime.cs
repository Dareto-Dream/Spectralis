using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal sealed class AlbumTrackPlayRequest
{
    public string TrackId { get; init; } = "";
    public double PositionSeconds { get; init; }
}

internal sealed class AlbumBookmarkRequest
{
    public string TrackId { get; init; } = "";
    public double PositionSeconds { get; init; }
    public string Label { get; init; } = "";
}

internal sealed class AlbumWorldRuntime : IDisposable
{
    private static readonly JsonSerializerOptions SerializeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AlbumManifest? manifest;
    private string? albumDir;
    private AlbumWorldSession? session;
    private AlbumTrackEntry? currentTrack;
    private bool isPlaying;
    private double trackPlayStartPosition;
    private long trackPlayStartTick;
    private long lastTickMs;

    public bool IsActive => manifest is not null;
    public string? CurrentTrackId => currentTrack?.Id;
    public AlbumManifest? Manifest => manifest;
    public string? AlbumDir => albumDir;
    public AlbumWorldSession? Session => session;

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? ExitWorldRequested;

    public void Load(AlbumManifest manifest, string albumDir, AlbumWorldSession session)
    {
        this.manifest = manifest;
        this.albumDir = albumDir;
        this.session = session;
        currentTrack = session.CurrentTrackId is not null
            ? manifest.Tracks.FirstOrDefault(t => t.Id == session.CurrentTrackId)
            : null;
        isPlaying = false;
        trackPlayStartTick = 0;
    }

    public void Unload()
    {
        manifest = null;
        albumDir = null;
        session = null;
        currentTrack = null;
        isPlaying = false;
    }

    public void NotifyTrackStarted(string trackId, double positionSeconds)
    {
        if (manifest is null || session is null)
            return;

        currentTrack = manifest.Tracks.FirstOrDefault(t => t.Id == trackId);
        isPlaying = true;
        trackPlayStartPosition = positionSeconds;
        trackPlayStartTick = Environment.TickCount64;
        lastTickMs = trackPlayStartTick;

        if (!session.TrackStats.ContainsKey(trackId))
            session.TrackStats[trackId] = new AlbumTrackStats();

        session.TrackStats[trackId].LastPlayedUtc = DateTimeOffset.UtcNow;
        session.CurrentTrackId = trackId;
        session.CurrentPositionSeconds = positionSeconds;
        session.LastPlayedUtc = DateTimeOffset.UtcNow;
    }

    public void NotifyPlaybackStateChanged(bool playing, double position)
    {
        if (!IsActive)
            return;

        if (isPlaying && !playing && currentTrack is not null)
            FlushAccumulatedPlayTime(position);

        isPlaying = playing;
        if (playing)
        {
            trackPlayStartPosition = position;
            trackPlayStartTick = Environment.TickCount64;
            lastTickMs = trackPlayStartTick;
        }

        if (session is not null)
            session.CurrentPositionSeconds = position;
    }

    public void Tick(double enginePosition, bool engineIsPlaying)
    {
        if (!IsActive || currentTrack is null || session is null)
            return;

        var now = Environment.TickCount64;
        var dtMs = now - lastTickMs;
        lastTickMs = now;

        if (isPlaying && engineIsPlaying && dtMs > 0 && dtMs < 5000)
        {
            if (!session.TrackStats.TryGetValue(currentTrack.Id, out var stats))
            {
                stats = new AlbumTrackStats();
                session.TrackStats[currentTrack.Id] = stats;
            }

            stats.PlayedSeconds += dtMs / 1000.0;
        }

        session.CurrentPositionSeconds = enginePosition;
        isPlaying = engineIsPlaying;
    }

    public void NotifyTrackCompleted(string trackId)
    {
        if (session is null)
            return;

        if (!session.TrackStats.TryGetValue(trackId, out var stats))
        {
            stats = new AlbumTrackStats();
            session.TrackStats[trackId] = stats;
        }

        stats.Completed = true;
        session.CurrentPositionSeconds = 0;
    }

    public void HandleWorldMessage(string messageJson)
    {
        if (!IsActive)
        {
            SpectralisLog.Warn("[.spectral] Ignored world message while runtime inactive");
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp))
                return;

            var type = typeProp.GetString() ?? "";
            SpectralisLog.Info($"[.spectral] Handling world message type={type}");
            switch (type)
            {
                case "spectral.playTrack":
                {
                    if (!root.TryGetProperty("trackId", out var idProp))
                        return;
                    var trackId = idProp.GetString() ?? "";
                    var position = root.TryGetProperty("positionSeconds", out var posProp)
                        ? posProp.GetDouble()
                        : 0.0;
                    PlayTrackRequested?.Invoke(this, new AlbumTrackPlayRequest { TrackId = trackId, PositionSeconds = position });
                    break;
                }

                case "spectral.addToQueue":
                {
                    if (!root.TryGetProperty("trackId", out var idProp))
                        return;
                    AddToQueueRequested?.Invoke(this, idProp.GetString() ?? "");
                    break;
                }

                case "spectral.pause":
                    PauseRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.resume":
                    ResumeRequested?.Invoke(this, EventArgs.Empty);
                    break;

                case "spectral.seek":
                {
                    if (!root.TryGetProperty("positionSeconds", out var posProp))
                        return;
                    SeekRequested?.Invoke(this, posProp.GetDouble());
                    break;
                }

                case "spectral.saveBookmark":
                {
                    if (session is null)
                        return;
                    var trackId = root.TryGetProperty("trackId", out var idProp) ? idProp.GetString() ?? "" : "";
                    var position = root.TryGetProperty("positionSeconds", out var posProp) ? posProp.GetDouble() : 0;
                    var label = root.TryGetProperty("label", out var lblProp) ? lblProp.GetString() ?? "" : "";
                    session.Bookmarks.Add(new AlbumBookmark
                    {
                        TrackId = trackId,
                        PositionSeconds = position,
                        Label = label,
                        CreatedUtc = DateTimeOffset.UtcNow
                    });
                    break;
                }

                case "spectral.exitWorld":
                    ExitWorldRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] Could not handle world message: {ex.Message}");
        }
    }

    public string BuildReadyScript()
    {
        var stateJson = BuildWorldStateJson();
        return $"if (typeof window.spectral?.onReady === 'function') window.spectral.onReady({stateJson});";
    }

    public string BuildTrackChangedScript(string trackId)
    {
        var track = manifest?.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
            return "";

        var trackState = new
        {
            id = track.Id,
            title = track.Title,
            artist = track.Artist,
            durationSeconds = track.Audio.DurationSeconds
        };
        var json = JsonSerializer.Serialize(trackState, SerializeOptions);
        return $"if (typeof window.spectral?.onTrackChanged === 'function') window.spectral.onTrackChanged({json});";
    }

    public string BuildTrackCompletedScript(string trackId)
    {
        var stats = session?.TrackStats.TryGetValue(trackId, out var s) == true ? s : null;
        var payload = new
        {
            trackId,
            playedSeconds = stats?.PlayedSeconds ?? 0
        };
        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        return $"if (typeof window.spectral?.onTrackCompleted === 'function') window.spectral.onTrackCompleted({json});";
    }

    public string BuildFrameScript(VisualizerFrame frame, bool playing, float position)
    {
        var levels = SampleSpectrum(frame.Spectrum, 32);
        var payload = new
        {
            levels,
            peak = Math.Clamp(frame.PeakLevel, 0f, 1.25f),
            rms = Math.Clamp(frame.RmsLevel, 0f, 1.25f),
            active = playing,
            time = (double)position,
            trackId = currentTrack?.Id ?? ""
        };
        var json = JsonSerializer.Serialize(payload, SerializeOptions);
        return $"if (typeof window.spectral?.onPlaybackFrame === 'function') window.spectral.onPlaybackFrame({json});";
    }

    public string BuildBootstrapScript()
    {
        return """
            window.spectral = window.spectral || {};
            window.spectral.onReady = window.spectral.onReady || function() {};
            window.spectral.onTrackChanged = window.spectral.onTrackChanged || function() {};
            window.spectral.onPlaybackFrame = window.spectral.onPlaybackFrame || function() {};
            window.spectral.onTrackCompleted = window.spectral.onTrackCompleted || function() {};
            window.spectral.onSessionRestored = window.spectral.onSessionRestored || function() {};
            """;
    }

    public (AudioTrackInfo? trackInfo, string? reactivePath) BuildTrackAssets(string trackId)
    {
        if (manifest is null || albumDir is null)
            return (null, null);

        var track = manifest.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null)
        {
            SpectralisLog.Warn($"[.spectral] Manifest has no track id={trackId}");
            return (null, null);
        }

        var audioPath = Path.GetFullPath(Path.Combine(albumDir, track.Audio.Entry));
        if (!File.Exists(audioPath))
        {
            SpectralisLog.Warn($"[.spectral] Audio file missing for track id={trackId}: {audioPath}");
            return (null, null);
        }

        var embeddedMeta = AudioMetadataReader.Read(audioPath);

        // Album art: prefer track asset image, then embedded
        byte[]? artBytes = embeddedMeta.AlbumArtBytes;
        var artEntry = track.Assets.Images.FirstOrDefault();
        if (artEntry is not null)
        {
            var artPath = Path.Combine(albumDir, artEntry);
            if (File.Exists(artPath))
                artBytes = File.ReadAllBytes(artPath);
        }

        // Lyrics: prefer track LRC sidecar, then embedded
        LyricsDocument? lyrics = embeddedMeta.Lyrics;
        var lrcEntry = track.Assets.Data
            .FirstOrDefault(d => d.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase));
        if (lrcEntry is not null)
        {
            var lrcPath = Path.Combine(albumDir, lrcEntry);
            if (File.Exists(lrcPath))
            {
                try
                {
                    var lrcText = File.ReadAllText(lrcPath, Encoding.UTF8);
                    lyrics = LrcParser.Parse(lrcText, "album-capsule");
                }
                catch { }
            }
        }

        // Per-track HTML visualizer
        var htmlContext = TryBuildTrackHtmlContext(track);

        var trackInfo = new AudioTrackInfo(
            FilePath: audioPath,
            DisplayName: string.IsNullOrWhiteSpace(track.Title)
                ? embeddedMeta.Title ?? Path.GetFileNameWithoutExtension(audioPath)
                : track.Title,
            Artist: string.IsNullOrWhiteSpace(track.Artist)
                ? embeddedMeta.Artist
                : track.Artist,
            Album: string.IsNullOrWhiteSpace(manifest.Title)
                ? embeddedMeta.Album
                : manifest.Title,
            AlbumArtBytes: artBytes,
            Lyrics: lyrics,
            EmbeddedVisualizer: embeddedMeta.EmbeddedVisualizer,
            EmbeddedTheme: embeddedMeta.EmbeddedTheme,
            EmbeddedHtml: htmlContext ?? embeddedMeta.EmbeddedHtml,
            EmbeddedMarkdown: embeddedMeta.EmbeddedMarkdown,
            EmbeddedVideo: embeddedMeta.EmbeddedVideo,
            FormatName: "Spectralis Album",
            Channels: 2,
            SourceSampleRate: 44100,
            BitsPerSample: 16,
            Duration: TimeSpan.FromSeconds(track.Audio.DurationSeconds),
            SuppressAppLyrics: track.SuppressAppLyrics);

        // Reactive timeline path
        string? reactivePath = null;
        var reactiveEntry = track.Assets.Data
            .FirstOrDefault(d => d.EndsWith("reactive.json", StringComparison.OrdinalIgnoreCase));
        if (reactiveEntry is null)
        {
            var candidate = Path.Combine(albumDir, Path.GetDirectoryName(track.Audio.Entry) ?? "", "reactive.json");
            if (File.Exists(candidate))
                reactivePath = candidate;
        }
        else
        {
            var candidate = Path.Combine(albumDir, reactiveEntry);
            if (File.Exists(candidate))
                reactivePath = candidate;
        }

        return (trackInfo, reactivePath);
    }

    public AlbumTrackEntry? FindTrack(string trackId) =>
        manifest?.Tracks.FirstOrDefault(t => t.Id == trackId);

    public void SaveSession()
    {
        if (session is null || albumDir is null)
            return;

        AlbumWorldSessionStore.Save(albumDir, session);
    }

    public void Dispose()
    {
        SaveSession();
        Unload();
    }

    private string BuildWorldStateJson()
    {
        if (manifest is null || session is null)
            return "{}";

        var tracks = manifest.Tracks.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            artist = t.Artist,
            durationSeconds = t.Audio.DurationSeconds
        }).ToArray();

        var trackStats = session.TrackStats.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)new { playedSeconds = kvp.Value.PlayedSeconds, completed = kvp.Value.Completed });

        var state = new
        {
            albumId = manifest.Id,
            title = manifest.Title,
            artist = manifest.Artist,
            tracks,
            session = new
            {
                currentTrackId = session.CurrentTrackId,
                currentPositionSeconds = session.CurrentPositionSeconds,
                trackStats
            }
        };

        return JsonSerializer.Serialize(state, SerializeOptions);
    }

    private EmbeddedHtmlContext? TryBuildTrackHtmlContext(AlbumTrackEntry track)
    {
        if (albumDir is null)
            return null;

        foreach (var vizElem in track.Visualizers)
        {
            if (vizElem.ValueKind != JsonValueKind.Object)
                continue;

            if (!vizElem.TryGetProperty("type", out var typeProp) ||
                !string.Equals(typeProp.GetString(), "html", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!vizElem.TryGetProperty("binaryEntry", out var entryProp))
                continue;

            var entryPath = entryProp.GetString();
            if (string.IsNullOrWhiteSpace(entryPath))
                continue;

            var htmlPath = Path.Combine(albumDir, entryPath);
            if (!File.Exists(htmlPath))
                continue;

            var htmlBytes = File.ReadAllBytes(htmlPath);
            if (htmlBytes.Length == 0)
                continue;

            var id = TryGetStringProperty(vizElem, "id");
            var version = TryGetStringProperty(vizElem, "version");

            var binaryAssets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (vizElem.TryGetProperty("binaryAssets", out var binaryAssetsElem) &&
                binaryAssetsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in binaryAssetsElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var assetEntryPath = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(assetEntryPath)) continue;
                    var assetPath = Path.Combine(albumDir, assetEntryPath);
                    if (File.Exists(assetPath))
                        binaryAssets[prop.Name] = File.ReadAllBytes(assetPath);
                }
            }

            var textAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (vizElem.TryGetProperty("dataAssets", out var dataAssetsElem) &&
                dataAssetsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in dataAssetsElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var assetEntryPath = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(assetEntryPath)) continue;
                    var assetPath = Path.Combine(albumDir, assetEntryPath);
                    if (File.Exists(assetPath))
                        textAssets[prop.Name] = File.ReadAllText(assetPath, Encoding.UTF8);
                }
            }

            return new EmbeddedHtmlContext(
                string.IsNullOrWhiteSpace(id) ? "album-track-html" : id,
                htmlBytes,
                binaryAssets,
                textAssets,
                string.IsNullOrWhiteSpace(version) ? null : version);
        }

        return null;
    }

    private static string? TryGetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private void FlushAccumulatedPlayTime(double currentPosition)
    {
        if (currentTrack is null || session is null)
            return;

        var elapsed = (Environment.TickCount64 - trackPlayStartTick) / 1000.0;
        if (elapsed <= 0 || elapsed > 3600)
            return;

        if (!session.TrackStats.TryGetValue(currentTrack.Id, out var stats))
        {
            stats = new AlbumTrackStats();
            session.TrackStats[currentTrack.Id] = stats;
        }

        stats.PlayedSeconds = Math.Max(stats.PlayedSeconds, stats.PlayedSeconds + elapsed);
    }

    private static float[] SampleSpectrum(float[] spectrum, int count)
    {
        if (spectrum is null || spectrum.Length == 0)
            return new float[count];

        var result = new float[count];
        var ratio = (double)spectrum.Length / count;
        for (var i = 0; i < count; i++)
        {
            var src = (int)(i * ratio);
            result[i] = Math.Clamp(spectrum[Math.Min(src, spectrum.Length - 1)], 0, 1.25f);
        }
        return result;
    }
}
