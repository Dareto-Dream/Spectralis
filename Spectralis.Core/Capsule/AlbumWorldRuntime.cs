using System.Text;
using System.Text.Json;
using Spectralis.Core.Common;
using Spectralis.Core.Embedded;
using Spectralis.Core.Metadata;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Capsule;

public sealed class AlbumTrackPlayRequest
{
    public string TrackId { get; init; } = "";
    public double PositionSeconds { get; init; }
}

/// <summary>One achievement board entry — see <see cref="AlbumWorldRuntime.BuildAchievementsSnapshot"/>.
/// <see cref="Hidden"/> entries should render as an unrevealed "???" card until <see cref="Unlocked"/>;
/// non-hidden ones (one per track) always show their real title/description regardless of lock state.</summary>
public sealed record AlbumAchievementEntry(string Id, string Title, string Description, bool Unlocked, bool Hidden);

/// <summary>
/// Runtime coordinator for an open .spectral album capsule.
/// Tracks session state, routes playback events from the app to the world JS bridge,
/// and builds per-track TrackInfo objects from the extracted album directory.
/// Ported from the legacy WinForms AlbumWorldRuntime.
/// </summary>
public sealed class AlbumWorldRuntime : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private AlbumManifest? _manifest;
    private string? _albumDir;
    private AlbumWorldSession? _session;
    private AlbumTrackEntry? _currentTrack;
    private bool _isPlaying;
    private long _lastTickMs;

    public bool IsActive => _manifest is not null;
    public string? CurrentTrackId => _currentTrack?.Id;
    public AlbumManifest? Manifest => _manifest;
    public string? AlbumDir => _albumDir;
    public AlbumWorldSession? Session => _session;

    public event EventHandler<AlbumTrackPlayRequest>? PlayTrackRequested;
    public event EventHandler<string>? AddToQueueRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler<double>? SeekRequested;
    public event EventHandler? ExitWorldRequested;

    public void Load(AlbumManifest manifest, string albumDir, AlbumWorldSession session)
    {
        _manifest = manifest;
        _albumDir = albumDir;
        _session = session;
        _currentTrack = !string.IsNullOrWhiteSpace(session.CurrentTrackId)
            ? manifest.Tracks.FirstOrDefault(t => string.Equals(t.Id, session.CurrentTrackId, StringComparison.OrdinalIgnoreCase))
            : null;
        _isPlaying = false;
        _lastTickMs = 0;
    }

    public void Unload()
    {
        _manifest = null;
        _albumDir = null;
        _session = null;
        _currentTrack = null;
        _isPlaying = false;
    }

    public void NotifyTrackStarted(string trackId, double positionSeconds)
    {
        if (_manifest is null || _session is null) return;

        _currentTrack = _manifest.Tracks.FirstOrDefault(t => t.Id == trackId);
        _isPlaying = true;
        _lastTickMs = Environment.TickCount64;

        if (!_session.TrackStats.TryGetValue(trackId, out var stats))
        {
            stats = new AlbumTrackStats();
            _session.TrackStats[trackId] = stats;
        }

        stats.PlayCount++;
        stats.LastPlayedUtc = DateTime.UtcNow;
        _session.CurrentTrackId = trackId;
        _session.CurrentPositionSeconds = Math.Max(0, positionSeconds);
        _session.LastOpenedUtc = DateTime.UtcNow;
    }

    public void NotifyPlaybackStateChanged(bool playing)
    {
        _isPlaying = playing;
    }

    public void NotifyTrackCompleted(string trackId)
    {
        if (_session is not null)
        {
            if (!_session.TrackStats.TryGetValue(trackId, out var stats))
            {
                stats = new AlbumTrackStats();
                _session.TrackStats[trackId] = stats;
            }

            stats.Completed = true;
            _session.CurrentPositionSeconds = 0;
        }

        _currentTrack = null;
        _isPlaying = false;
    }

    public void Tick(double enginePosition, bool engineIsPlaying)
    {
        if (_currentTrack is null || _session is null) return;

        var nowMs = Environment.TickCount64;
        if (_isPlaying && _lastTickMs > 0)
        {
            var elapsedSeconds = (nowMs - _lastTickMs) / 1000.0;
            if (elapsedSeconds > 0 && elapsedSeconds < 2.0)
            {
                if (!_session.TrackStats.TryGetValue(_currentTrack.Id, out var tickStats))
                {
                    tickStats = new AlbumTrackStats();
                    _session.TrackStats[_currentTrack.Id] = tickStats;
                }
                tickStats.PlayedSeconds += elapsedSeconds;
            }
        }

        _isPlaying = engineIsPlaying;
        _lastTickMs = nowMs;
        _session.CurrentPositionSeconds = Math.Max(0, enginePosition);
    }

    public void HandleWorldMessage(string messageJson)
    {
        if (!IsActive) return;

        try
        {
            using var doc = JsonDocument.Parse(messageJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            switch (typeProp.GetString() ?? "")
            {
                case "spectral.playTrack":
                {
                    if (!root.TryGetProperty("trackId", out var idProp)) return;
                    var pos = root.TryGetProperty("positionSeconds", out var posProp) ? posProp.GetDouble() : 0.0;
                    PlayTrackRequested?.Invoke(this, new AlbumTrackPlayRequest
                    {
                        TrackId = idProp.GetString() ?? "",
                        PositionSeconds = pos,
                    });
                    break;
                }
                case "spectral.addToQueue":
                {
                    if (!root.TryGetProperty("trackId", out var idProp)) return;
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
                    if (!root.TryGetProperty("positionSeconds", out var posProp)) return;
                    SeekRequested?.Invoke(this, posProp.GetDouble());
                    break;
                }
                case "spectral.saveBookmark":
                {
                    if (_albumDir is null) return;
                    var key = root.TryGetProperty("key", out var kp) ? kp.GetString() ?? "pos" : "pos";
                    var value = root.TryGetProperty("value", out var vp) ? vp.GetString() ?? "" : "";
                    AlbumWorldSessionStore.SaveBookmark(_albumDir, key, value);
                    break;
                }
                case "spectral.exitWorld":
                    ExitWorldRequested?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }
        catch { }
    }

    public string BuildBootstrapScript() => """
        window.spectral = window.spectral || {};
        window.spectral.onReady = window.spectral.onReady || function() {};
        window.spectral.onTrackChanged = window.spectral.onTrackChanged || function() {};
        window.spectral.onPlaybackFrame = window.spectral.onPlaybackFrame || function() {};
        window.spectral.onTrackCompleted = window.spectral.onTrackCompleted || function() {};
        window.spectral.onSessionRestored = window.spectral.onSessionRestored || function() {};
        """;

    public string BuildReadyScript()
    {
        var stateJson = BuildWorldStateJson();
        return $"if (typeof window.spectral?.onReady === 'function') window.spectral.onReady({stateJson});";
    }

    public string BuildTrackChangedScript(string trackId)
    {
        var track = _manifest?.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null) return "";
        var payload = new
        {
            id = track.Id,
            title = track.Title,
            artist = track.Artist,
            durationSeconds = track.Audio.DurationSeconds,
        };
        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        return $"if (typeof window.spectral?.onTrackChanged === 'function') window.spectral.onTrackChanged({json});";
    }

    public string BuildTrackCompletedScript(string trackId)
    {
        var json = JsonSerializer.Serialize(new { trackId }, _jsonOpts);
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
            trackId = _currentTrack?.Id ?? "",
        };
        var json = JsonSerializer.Serialize(payload, _jsonOpts);
        return $"if (typeof window.spectral?.onPlaybackFrame === 'function') window.spectral.onPlaybackFrame({json});";
    }

    /// <summary>
    /// Builds a TrackInfo for the given track from the extracted album directory.
    /// Returns null if the track id is not found or the audio file is missing.
    /// </summary>
    public TrackInfo? BuildTrackInfo(string trackId)
    {
        if (_manifest is null || _albumDir is null) return null;

        var track = _manifest.Tracks.FirstOrDefault(t => t.Id == trackId);
        if (track is null) return null;

        var audioPath = Path.GetFullPath(Path.Combine(_albumDir, track.Audio.Entry));
        if (!File.Exists(audioPath)) return null;

        // Read embedded metadata for lyrics/art from the audio file itself
        TrackInfo base_ = TryReadMetadata(audioPath) ?? new TrackInfo { SourcePath = audioPath };

        // Prefer capsule manifest values over embedded tags
        byte[]? artBytes = base_.CoverArt;
        string? artMime = base_.CoverArtMimeType;
        var artEntry = track.Assets.Images.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(artEntry))
        {
            var artPath = SafePath(_albumDir, artEntry);
            if (artPath is not null && File.Exists(artPath))
            {
                artBytes = File.ReadAllBytes(artPath);
                artMime = GuessMime(artEntry);
            }
        }

        var htmlContext = TryBuildTrackHtmlContext(track);

        return base_ with
        {
            SourcePath = audioPath,
            Title = FirstNonEmpty(track.Title, base_.Title, Path.GetFileNameWithoutExtension(audioPath)),
            Artist = FirstNonEmpty(track.Artist, _manifest.Artist, base_.Artist),
            Album = FirstNonEmpty(_manifest.Title, base_.Album),
            Duration = track.Audio.DurationSeconds > 0
                ? TimeSpan.FromSeconds(track.Audio.DurationSeconds)
                : base_.Duration,
            CoverArt = artBytes,
            CoverArtMimeType = artMime,
            FormatName = "Spectralis Album",
            EmbeddedHtml = htmlContext ?? base_.EmbeddedHtml,
        };
    }

    /// <summary>
    /// Builds an EmbeddedHtmlContext for the album world HTML entry, if declared.
    /// This is the persistent "world" HTML that shows across all tracks.
    /// </summary>
    public EmbeddedHtmlContext? BuildWorldHtmlContext()
    {
        if (_manifest?.World?.Entry is not { Length: > 0 } entry || _albumDir is null)
            return null;

        // World HTML may live in a "world/" subfolder or directly in albumDir
        var worldFolder = Path.Combine(_albumDir, "world");
        string htmlPath;

        if (Directory.Exists(worldFolder) && File.Exists(Path.Combine(worldFolder, Path.GetFileName(entry))))
            htmlPath = Path.Combine(worldFolder, Path.GetFileName(entry));
        else
            htmlPath = Path.Combine(_albumDir, entry);

        if (!File.Exists(htmlPath)) return null;

        var htmlBytes = File.ReadAllBytes(htmlPath);
        if (htmlBytes.Length == 0) return null;

        var binaryAssets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, assetEntry) in _manifest.World.BinaryAssets)
        {
            var p = SafePath(_albumDir, assetEntry);
            if (p is not null && File.Exists(p))
                binaryAssets[key] = File.ReadAllBytes(p);
        }

        var textAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, assetEntry) in _manifest.World.DataAssets)
        {
            var p = SafePath(_albumDir, assetEntry);
            if (p is not null && File.Exists(p))
                textAssets[key] = File.ReadAllText(p, Encoding.UTF8);
        }

        return new EmbeddedHtmlContext(
            "album-world",
            htmlBytes,
            binaryAssets,
            textAssets,
            null,
            Path.GetDirectoryName(htmlPath) ?? _albumDir,
            // Without this, Capabilities defaults to empty and every capability-gated feature
            // (worlds.wasm3d, audio.dspPreset, presence.richPresence, worlds.pointerLock) reads
            // as denied for every album world regardless of what its manifest actually declares
            // — caught by pointer lock coming back denied on a manifest that plainly requested it.
            _manifest?.Capabilities,
            ResolvePauseMenuConfig(),
            ResolveContentWarning());
    }

    /// <summary>Maps <c>world.contentWarning</c> to the host-facing record. Unlike the pause menu
    /// this is not capability-gated. A block with no title and no items is treated as absent, and
    /// the key hashes every word of it so editing the text re-prompts listeners.</summary>
    private EmbeddedContentWarning? ResolveContentWarning()
    {
        if (_manifest?.World?.ContentWarning is not { } cfg)
        {
            return null;
        }

        var items = cfg.Items
            .Where(i => !string.IsNullOrWhiteSpace(i.Heading) || !string.IsNullOrWhiteSpace(i.Detail))
            .Select(i => new EmbeddedContentWarningItem(i.Heading.Trim(), i.Detail.Trim()))
            .ToList();
        if (items.Count == 0 && string.IsNullOrWhiteSpace(cfg.Title) && string.IsNullOrWhiteSpace(cfg.Intro))
        {
            return null;
        }

        var title = string.IsNullOrWhiteSpace(cfg.Title) ? "Content warning" : cfg.Title.Trim();
        var accept = string.IsNullOrWhiteSpace(cfg.AcceptLabel) ? "I understand, continue" : cfg.AcceptLabel.Trim();
        var decline = string.IsNullOrWhiteSpace(cfg.DeclineLabel) ? "Go back" : cfg.DeclineLabel.Trim();
        var text = string.Join('\n', new[] { _manifest.Id, title, cfg.Intro, cfg.Outro }
            .Concat(items.SelectMany(i => new[] { i.Heading, i.Detail })));
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        return new EmbeddedContentWarning(title, cfg.Intro?.Trim(), items, cfg.Outro?.Trim(), accept, decline, key);
    }

    /// <summary>Maps the manifest's own <c>world.pauseMenu</c> block to the capability-agnostic
    /// <see cref="EmbeddedPauseMenuConfig"/> the host surface reads — but only when the manifest
    /// also declares <c>worlds.pauseMenu</c>; a capsule that never requested that capability gets
    /// null here regardless of what's sitting in its manifest, same "declared but not granted is
    /// silently ignored" shape as every other capability gate in this class.</summary>
    private EmbeddedPauseMenuConfig? ResolvePauseMenuConfig()
    {
        if (_manifest is null || _manifest.World?.PauseMenu is not { } cfg)
        {
            return null;
        }

        if (!_manifest.Capabilities.Contains(CapsuleCapability.WorldsPauseMenu, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return new EmbeddedPauseMenuConfig(cfg.Title, cfg.Message, cfg.ResumeLabel, cfg.ExitLabel);
    }

    /// <summary>True when this album declares a sandboxed Wasm/wgpu world alongside (or instead
    /// of) the HTML one — see <see cref="AlbumWorldSection.WasmEntry"/>.</summary>
    public bool HasWasmWorld => !string.IsNullOrWhiteSpace(_manifest?.World?.WasmEntry);

    /// <summary>
    /// Reads the Wasm module's raw bytes for <see cref="WasmWorldHost.Load"/>, or null if this
    /// album declares no Wasm world (<see cref="HasWasmWorld"/> false) or the file is missing.
    /// </summary>
    public byte[]? ReadWasmWorldBytes()
    {
        if (_manifest?.World?.WasmEntry is not { Length: > 0 } entry || _albumDir is null)
            return null;

        var path = SafePath(_albumDir, entry);
        return path is not null && File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>Builds the story EmbeddedHtmlContext from the album manifest, if applicable.</summary>
    public EmbeddedHtmlContext? BuildStoryHtmlContext()
    {
        if (_manifest is null || _albumDir is null) return null;

        return CapsuleStoryRenderer.TryToHtmlContext(_manifest.Story, entry =>
        {
            var p = SafePath(_albumDir, entry);
            return p is not null && File.Exists(p) ? File.ReadAllBytes(p) : null;
        });
    }

    public void SaveSession()
    {
        if (_session is null || _albumDir is null) return;
        AlbumWorldSessionStore.SaveSession(_albumDir, _session);
    }

    public void Dispose()
    {
        SaveSession();
        Unload();
    }

    // ── internal helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Serialized album state passed to <c>window.spectral.onReady()</c> after the
    /// world HTML navigates.  Public so the app layer can inject it via the webview service.
    /// </summary>
    public string BuildWorldStateJson()
    {
        if (_manifest is null || _session is null) return "{}";

        var tracks = _manifest.Tracks.Select(t => new
        {
            id = t.Id,
            title = t.Title,
            artist = t.Artist,
            durationSeconds = t.Audio.DurationSeconds,
        }).ToArray();

        var trackStats = _session.TrackStats.ToDictionary(
            kvp => kvp.Key,
            kvp => (object)new
            {
                playCount = kvp.Value.PlayCount,
                playedSeconds = kvp.Value.PlayedSeconds,
                completed = kvp.Value.Completed,
            });

        var state = new
        {
            albumId = _manifest.Id,
            title = _manifest.Title,
            artist = _manifest.Artist,
            tracks,
            session = new
            {
                currentTrackId = _session.CurrentTrackId,
                currentPositionSeconds = _session.CurrentPositionSeconds,
                introCompleted = _session.IntroCompleted,
                trackStats,
                unlockedAchievements = _session.UnlockedAchievements,
                levelGateProgress = _session.LevelGateProgress,
            },
        };

        return JsonSerializer.Serialize(state, _jsonOpts);
    }

    /// <summary>
    /// Builds the achievements board's entries: one per track (unlocked once that track's been
    /// played at least once — reuses <see cref="AlbumTrackStats.PlayCount"/>, already tracked by
    /// every <see cref="NotifyTrackStarted"/> call, no separate bookkeeping needed) plus a
    /// handful of hidden ones. Two of the hidden entries are real persisted achievement ids
    /// (<c>walked-into-chaser</c>, <c>found-the-mirror</c> — see chaser_room's <c>lib.rs</c>); the
    /// third (<c>heard-them-all</c>) is derived here from track stats rather than stored, since
    /// "every track has been played" is fully computable from state the session already has.
    /// </summary>
    public IReadOnlyList<AlbumAchievementEntry> BuildAchievementsSnapshot()
    {
        if (_manifest is null || _session is null)
        {
            return [];
        }

        var entries = new List<AlbumAchievementEntry>();

        foreach (var track in _manifest.Tracks)
        {
            var played = _session.TrackStats.TryGetValue(track.Id, out var stats) && stats.PlayCount > 0;
            entries.Add(new AlbumAchievementEntry(
                $"played-{track.Id}",
                track.Title,
                $"Play \"{track.Title}\"",
                played,
                Hidden: false));
        }

        var unlocked = _session.UnlockedAchievements;
        entries.Add(new AlbumAchievementEntry(
            "walked-into-chaser", "Threshold", "Set foot in the room.",
            unlocked.Contains("walked-into-chaser"), Hidden: true));
        entries.Add(new AlbumAchievementEntry(
            "found-the-mirror", "Cracked", "Found what's left of the mirror.",
            unlocked.Contains("found-the-mirror"), Hidden: true));

        var heardThemAll = _manifest.Tracks.Count > 0 && _manifest.Tracks.All(track =>
            _session.TrackStats.TryGetValue(track.Id, out var stats) && stats.PlayCount > 0);
        entries.Add(new AlbumAchievementEntry(
            "heard-them-all", "Full Playthrough", "Play every track in the room.",
            heardThemAll, Hidden: true));

        return entries;
    }

    private EmbeddedHtmlContext? TryBuildTrackHtmlContext(AlbumTrackEntry track)
    {
        if (_albumDir is null) return null;

        foreach (var vizElem in track.Visualizers)
        {
            if (vizElem.ValueKind != JsonValueKind.Object) continue;
            if (!vizElem.TryGetProperty("type", out var typeProp) ||
                !string.Equals(typeProp.GetString(), "html", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!vizElem.TryGetProperty("binaryEntry", out var entryProp)) continue;
            var entryPath = entryProp.GetString();
            if (string.IsNullOrWhiteSpace(entryPath)) continue;

            var htmlPath = SafePath(_albumDir, entryPath);
            if (htmlPath is null || !File.Exists(htmlPath)) continue;

            var htmlBytes = File.ReadAllBytes(htmlPath);
            if (htmlBytes.Length == 0) continue;

            var binaryAssets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (vizElem.TryGetProperty("binaryAssets", out var binElem) &&
                binElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in binElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var p = SafePath(_albumDir, prop.Value.GetString() ?? "");
                    if (p is not null && File.Exists(p))
                        binaryAssets[prop.Name] = File.ReadAllBytes(p);
                }
            }

            var id = vizElem.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "album-track-html" : "album-track-html";
            return new EmbeddedHtmlContext(id, htmlBytes, binaryAssets, null, null);
        }

        return null;
    }

    /// <summary>
    /// Returns an absolute path under <paramref name="baseDir"/> for the given relative entry,
    /// or null if the result would escape the base directory (path traversal guard).
    /// </summary>
    private static string? SafePath(string baseDir, string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return null;
        var full = Path.GetFullPath(Path.Combine(baseDir, entry.Replace('\\', '/')));
        var root = Path.GetFullPath(baseDir);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private static TrackInfo? TryReadMetadata(string path)
    {
        try { return TrackMetadataReader.Read(path); }
        catch { return null; }
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
            if (!string.IsNullOrWhiteSpace(v)) return v.Trim();
        return string.Empty;
    }

    private static string GuessMime(string filename) =>
        Path.GetExtension(filename).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp"           => "image/webp",
            ".gif"            => "image/gif",
            ".bmp"            => "image/bmp",
            _                 => "image/png",
        };

    private static float[] SampleSpectrum(float[] spectrum, int count)
    {
        if (spectrum is null || spectrum.Length == 0) return new float[count];
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
