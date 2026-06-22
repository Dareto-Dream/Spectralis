using System.Security.Cryptography;
using Avalonia.Threading;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Spectralis.Core.Integrations.Obs;
using Spectralis.Core.SongWars;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services;

/// <summary>
/// Runs the OBS overlay HTTP server (same port and API as the WinForms app:
/// http://127.0.0.1:5128/obs/{token}) and pushes playback/lyrics/visualizer
/// state to it on a UI-timer cadence.
/// </summary>
public sealed class ObsOverlayCoordinator : IDisposable
{
    public const int Port = 5128;

    private readonly AudioEngine _engine;
    private readonly NowPlayingViewModel _nowPlaying;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _pushTimer;
    private ObsOverlayServer? _server;
    private string _artworkVersion = string.Empty;
    private byte[]? _lastArtwork;

    public ObsOverlayCoordinator(AudioEngine engine, NowPlayingViewModel nowPlaying, AppSettings settings)
    {
        _engine = engine;
        _nowPlaying = nowPlaying;
        _settings = settings;
        // 100ms (10Hz): client-side interpolation (see ObsOverlayHtml.cs) smooths between
        // pushes, but a tighter push interval keeps the chased target fresher and reduces lag.
        _pushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => PushState());
    }

    /// <summary>Optional callback to pull the active Song Wars tournament for the overlay bracket feed.
    /// Set by the shell when the Song Wars window is open.</summary>
    public Func<SongWarsTournament?>? GetActiveTournament { get; set; }

    public string? OverlayUrl => _server?.BaseUrl;

    public bool IsRunning => _server is not null;

    public string? StartupError { get; private set; }

    public void Start()
    {
        if (_server is not null)
        {
            return;
        }

        if (!_settings.EnableObsOverlay)
        {
            StartupError = "Disabled in settings.";
            return;
        }

        try
        {
            StartupError = null;
            _server = new ObsOverlayServer(
                _settings.ObsOverlayPort,
                LoadOrCreateToken(_settings),
                getLayout: id => ResolveLayout(id));
            _server.Start();
            _pushTimer.Start();
        }
        catch (Exception ex)
        {
            // Port already bound (e.g. the WinForms app is running) - degrade gracefully.
            StartupError = ex.Message;
            _server?.Dispose();
            _server = null;
        }
    }

    public void SetEnabled(bool enabled)
    {
        _settings.EnableObsOverlay = enabled;
        if (enabled)
        {
            Start();
            return;
        }

        Stop("Disabled in settings.");
    }

    public void Stop(string? reason = null)
    {
        _pushTimer.Stop();
        _server?.Dispose();
        _server = null;
        StartupError = reason;
    }

    public void Restart()
    {
        Stop();
        Start();
    }

    private void PushState()
    {
        if (_server is null)
        {
            return;
        }

        var track = _engine.CurrentTrack;
        var frame = _engine.GetVisualizerFrame();

        byte[]? artwork = null;
        var artworkChanged = false;
        if (!ReferenceEquals(track?.CoverArt, _lastArtwork))
        {
            _lastArtwork = track?.CoverArt;
            artwork = track?.CoverArt;
            artworkChanged = true;
            _artworkVersion = Guid.NewGuid().ToString("N")[..8]; // cache-busting
        }

        var (current, next) = CurrentAndNextLyric();

        _server.UpdateState(new ObsOverlayState
        {
            Track = new ObsTrackState
            {
                Title = track?.DisplayTitle ?? string.Empty,
                Artist = track?.Artist ?? string.Empty,
                Album = track?.Album ?? string.Empty,
                DurationSeconds = _engine.GetLength(),
                ArtworkVersion = _artworkVersion,
            },
            Playback = new ObsPlaybackState
            {
                IsPlaying = _engine.IsPlaying,
                PositionSeconds = _engine.GetPosition(),
                Volume = _engine.Volume,
            },
            Lyrics = new ObsLyricsState
            {
                Current = current,
                Next = next,
                Progress = _engine.GetLength() > 0 ? _engine.GetPosition() / _engine.GetLength() : 0,
            },
            Visualizer = new ObsVisualizerState
            {
                Levels = frame.Spectrum.Select(static level => (double)level).ToArray(),
                Rms = frame.RmsLevel,
                Peak = frame.PeakLevel,
            },
            SongWars = BuildObsSongWarsState(),
            LayoutSeq = _server.CurrentLayoutSeq,
        }, artwork, track?.CoverArtMimeType ?? "image/jpeg", artworkChanged);
    }

    private ObsSongWarsState BuildObsSongWarsState()
    {
        var tournament = GetActiveTournament?.Invoke();
        if (tournament is null)
            return new ObsSongWarsState();

        var current = SongWarsBracketEngine.GetCurrentMatch(tournament);
        var highlightId = current?.MatchId ?? "";

        var focusMatch = current;

        var submissions = tournament.Submissions
            .Select(static s => new ObsSongWarsSubmission
            {
                Id = s.SubmissionId,
                Title = s.DisplayTitle,
                Artist = s.ArtistDisplayName,
                Seed = s.Seed,
                Losses = s.Losses,
                Status = s.Status.ToString(),
            })
            .ToArray();

        var winner = tournament.CurrentMatchId is null
            ? tournament.Submissions.FirstOrDefault(static s =>
                s.Status is SongWarsSubmissionStatus.Active or SongWarsSubmissionStatus.LosersBracket)
            : null;

        var currentIndex = string.IsNullOrWhiteSpace(current?.MatchId)
            ? -1
            : tournament.MatchOrder.IndexOf(current!.MatchId);
        var nextMatch = tournament.MatchOrder
            .Skip(Math.Max(0, currentIndex + 1))
            .Select(id => tournament.Matches.FirstOrDefault(m =>
                string.Equals(m.MatchId, id, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(m => m is not null && m.Result == SongWarsOutcome.Pending);

        return new ObsSongWarsState
        {
            IsActive = true,
            TournamentId = tournament.TournamentId,
            Name = tournament.Name,
            CurrentMatchId = current?.MatchId ?? "",
            HighlightMatchId = highlightId,
            Phase = focusMatch is null ? "Complete" : FriendlyPhase(focusMatch.Phase),
            RoundLabel = focusMatch is null ? "Tournament Complete" : FriendlyRound(focusMatch),
            FocusSlot = focusMatch?.FocusSlot.ToString() ?? "",
            EliminationsUsed = tournament.EliminationCount,
            MaxEliminations = SongWarsVoteTally.MaxDirectEliminations,
            Submissions = submissions,
            Matches = tournament.Matches
                .OrderBy(static m => m.Bracket)
                .ThenBy(static m => m.RoundIndex)
                .ThenBy(m => tournament.MatchOrder.IndexOf(m.MatchId))
                .Select(m => ToObsMatch(tournament, m, current?.MatchId, highlightId))
                .ToArray(),
            NextMatch = nextMatch is null ? null : ToObsMatch(tournament, nextMatch, current?.MatchId, highlightId),
            Winner = winner is null ? null : new ObsSongWarsSubmission
            {
                Id = winner.SubmissionId,
                Title = winner.DisplayTitle,
                Artist = winner.ArtistDisplayName,
                Seed = winner.Seed,
                Losses = winner.Losses,
                Status = winner.Status.ToString(),
            },
        };
    }

    private static ObsSongWarsMatch ToObsMatch(
        SongWarsTournament tournament,
        SongWarsMatch match,
        string? currentMatchId,
        string? highlightMatchId)
    {
        var subA = FindSubmission(tournament, match.SlotASubmissionId);
        var subB = FindSubmission(tournament, match.SlotBSubmissionId);
        var winner = FindSubmission(tournament, match.WinnerSubmissionId);
        var snapshot = match.VoteSnapshots.LastOrDefault();

        return new ObsSongWarsMatch
        {
            Id = match.MatchId,
            Bracket = match.Bracket.ToString(),
            RoundIndex = match.RoundIndex,
            RoundId = match.RoundId,
            RoundLabel = FriendlyRound(match),
            SlotAId = match.SlotASubmissionId,
            SlotBId = match.SlotBSubmissionId,
            SlotATitle = subA?.DisplayTitle ?? "Track A",
            SlotBTitle = subB?.DisplayTitle ?? "Track B",
            SlotAArtist = subA?.ArtistDisplayName ?? "",
            SlotBArtist = subB?.ArtistDisplayName ?? "",
            Phase = FriendlyPhase(match.Phase),
            Result = match.Result.ToString(),
            WinnerId = match.WinnerSubmissionId ?? "",
            WinnerTitle = winner?.DisplayTitle ?? "",
            EliminatedSlot = snapshot?.EliminatedSlot?.ToString() ?? "",
            IsCurrent = string.Equals(match.MatchId, currentMatchId, StringComparison.OrdinalIgnoreCase),
            IsHighlighted = string.Equals(match.MatchId, highlightMatchId, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static SongWarsSubmission? FindSubmission(SongWarsTournament tournament, string? id) =>
        string.IsNullOrWhiteSpace(id)
            ? null
            : tournament.Submissions.FirstOrDefault(s =>
                string.Equals(s.SubmissionId, id, StringComparison.OrdinalIgnoreCase));

    private static string FriendlyRound(SongWarsMatch match) => match.Bracket switch
    {
        SongWarsBracket.Winners => $"Winners R{match.RoundIndex}",
        SongWarsBracket.Losers => $"Losers R{match.RoundIndex}",
        SongWarsBracket.GrandFinals => match.RoundIndex > 1 ? "Grand Reset" : "Grand Finals",
        _ => match.RoundId,
    };

    private static string FriendlyPhase(SongWarsMatchPhase phase) => phase switch
    {
        SongWarsMatchPhase.Pending => "Queued",
        SongWarsMatchPhase.Ready => "Ready",
        SongWarsMatchPhase.TrackAPlaying => "Track A playing",
        SongWarsMatchPhase.TrackBPlaying => "Track B playing",
        SongWarsMatchPhase.PrimaryVoting => "Voting open",
        SongWarsMatchPhase.EliminationVoting => "Elimination vote",
        SongWarsMatchPhase.Reveal => "Revealed",
        SongWarsMatchPhase.Complete => "Complete",
        SongWarsMatchPhase.Skipped => "Skipped",
        SongWarsMatchPhase.Paused => "Paused",
        _ => phase.ToString(),
    };

    private (string Current, string Next) CurrentAndNextLyric()
    {
        var index = _nowPlaying.ActiveLyricIndex;
        var lines = _nowPlaying.LyricsLines;
        var current = index >= 0 && index < lines.Count ? lines[index].Text : string.Empty;
        var next = index + 1 >= 0 && index + 1 < lines.Count ? lines[index + 1].Text : string.Empty;
        return (current, next);
    }

    private ObsLayout ResolveLayout(string? overlayId = null)
    {
        if (!string.IsNullOrWhiteSpace(overlayId)
            && _settings.ObsNamedLayouts.TryGetValue(overlayId, out var namedJson)
            && !string.IsNullOrWhiteSpace(namedJson))
        {
            var parsed = ObsLayout.FromJson(namedJson);
            if (parsed is not null) return parsed;
        }

        if (!string.IsNullOrWhiteSpace(_settings.ObsLayoutJson))
        {
            var parsed = ObsLayout.FromJson(_settings.ObsLayoutJson);
            if (parsed is not null) return parsed;
        }

        return ObsLayout.CreateDefault();
    }

    /// <summary>Persists a new layout for the default overlay and bumps the layout version.</summary>
    public void SetLayout(ObsLayout layout)
    {
        _settings.ObsLayoutJson = layout.ToJson();
        AppSettingsStore.Save(_settings);
        _server?.BumpLayoutVersion();
    }

    /// <summary>Persists a layout for a named overlay and bumps the layout version.</summary>
    public void SetNamedLayout(string id, ObsLayout layout)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _settings.ObsNamedLayouts[id] = layout.ToJson();
        AppSettingsStore.Save(_settings);
        _server?.BumpLayoutVersion();
    }

    /// <summary>Removes a named overlay layout and bumps the layout version.</summary>
    public void RemoveNamedLayout(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        _settings.ObsNamedLayouts.Remove(id);
        AppSettingsStore.Save(_settings);
        _server?.BumpLayoutVersion();
    }

    /// <summary>Returns the browser source URL for the given overlay id (empty = default overlay).</summary>
    public string? GetOverlayUrl(string id = "") => _server?.GetOverlayUrl(id);

    private static string LoadOrCreateToken(AppSettings settings)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Spectralis", "obs-overlay-token.txt");

        if (!string.IsNullOrWhiteSpace(settings.ObsOverlayToken))
        {
            return settings.ObsOverlayToken;
        }

        try
        {
            if (File.Exists(path))
            {
                var existing = File.ReadAllText(path).Trim();
                if (existing.Length >= 16)
                {
                    settings.ObsOverlayToken = existing;
                    AppSettingsStore.Save(settings);
                    return existing;
                }
            }
        }
        catch
        {
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        settings.ObsOverlayToken = token;
        AppSettingsStore.Save(settings);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, token);
        }
        catch
        {
        }

        return token;
    }

    public void Dispose()
    {
        Stop();
    }
}
