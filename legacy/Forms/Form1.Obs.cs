using System.Drawing;
using System.IO;

namespace Spectralis;

public partial class Form1
{
    private ObsOverlayServer? obsServer;
    private int obsServerPort;
    private string obsServerToken = "";
    private ToolStripMenuItem? obsToolStripMenuItem;
    private long nextObsTick;
    private const long ObsIntervalMs = 100;

    private void InitializeObs()
    {
        obsToolStripMenuItem = new ToolStripMenuItem
        {
            Text = "OBS Overlay...",
            Name = "obsToolStripMenuItem"
        };
        obsToolStripMenuItem.Click += (_, _) => ShowObsDialog();
        toolsToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
        toolsToolStripMenuItem.DropDownItems.Add(obsToolStripMenuItem);

        ApplyObsSettings();
    }

    private void ApplyObsSettings()
    {
        if (!appSettings.EnableObsOverlay)
        {
            obsServer?.Dispose();
            obsServer = null;
            return;
        }

        if (obsServer is not null &&
            obsServerPort == appSettings.ObsOverlayPort &&
            string.Equals(obsServerToken, appSettings.ObsOverlayToken, StringComparison.Ordinal))
        {
            return;
        }

        obsServer?.Dispose();
        obsServer = null;

        try
        {
            obsServer = new ObsOverlayServer(
                appSettings.ObsOverlayPort,
                appSettings.ObsOverlayToken,
                getLayout: GetCurrentObsLayout,
                getBannerHtml: GetVisualizerBannerHtml);
            obsServer.Start();
            obsServerPort = appSettings.ObsOverlayPort;
            obsServerToken = appSettings.ObsOverlayToken;
        }
        catch
        {
            obsServer?.Dispose();
            obsServer = null;
            obsServerPort = 0;
            obsServerToken = "";
        }
    }

    private ObsLayout GetCurrentObsLayout(string? overlayId)
    {
        if (!string.IsNullOrWhiteSpace(overlayId) && TryGetNamedObsLayout(overlayId, out var namedLayout))
        {
            namedLayout.AllowFallback = appSettings.ObsOverlayAllowMissingCustomBanner;
            return namedLayout;
        }

        return GetCurrentObsLayout();
    }

    private ObsLayout GetCurrentObsLayout()
    {
        var layout = ObsLayout.FromJson(appSettings.ObsOverlayLayout);
        if (layout is not null && layout.Widgets.Count > 0)
        {
            layout.AllowFallback = appSettings.ObsOverlayAllowMissingCustomBanner;
            return layout;
        }

        // Legacy fallback — convert old preset settings to a layout
        var legacy = BuildLegacyLayout();
        legacy.AllowFallback = appSettings.ObsOverlayAllowMissingCustomBanner;
        return legacy;
    }

    private bool TryGetNamedObsLayout(string overlayId, out ObsLayout layout)
    {
        var slug = ToObsOverlaySlug(overlayId);
        foreach (var preset in BuiltInObsPresets.All)
        {
            if (string.Equals(ToObsOverlaySlug(preset.Name), slug, StringComparison.OrdinalIgnoreCase) &&
                preset.Layout is { } presetLayout)
            {
                layout = presetLayout;
                return true;
            }
        }

        foreach (var preset in appSettings.ObsUserPresets)
        {
            if (string.Equals(ToObsOverlaySlug(preset.Name), slug, StringComparison.OrdinalIgnoreCase) &&
                preset.Layout is { } presetLayout)
            {
                layout = presetLayout;
                return true;
            }
        }

        layout = ObsLayout.CreateDefault();
        return false;
    }

    private ObsLayout BuildLegacyLayout()
    {
        var widgets = new List<ObsLayoutWidget>();

        if (appSettings.ObsOverlayShowNowPlaying)
        {
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.NowPlaying,
                X = 0.02, Y = 0.78, W = 0.30, H = 0.13,
                ShowArt = true, ShowArtist = true,
                ShowProgress = appSettings.ObsOverlayShowProgress,
                ArtShape = appSettings.ObsOverlayArtworkShape,
                BgOpacity = appSettings.ObsOverlayBackgroundOpacity,
                Radius = appSettings.ObsOverlayCornerRadius
            });
        }

        if (appSettings.ObsOverlayShowLyrics)
        {
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Lyrics,
                X = 0.25, Y = 0.88, W = 0.50, H = 0.09,
                ShowNext = appSettings.ObsOverlayShowNextLyric,
                BgOpacity = 0
            });
        }

        if (appSettings.ObsOverlayShowVisualizer)
        {
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum).Key,
                X = 0.02, Y = 0.68, W = 0.30, H = 0.09,
                BgOpacity = 0,
                VizIntensity = appSettings.ObsOverlayVisualizerIntensity
            });
        }

        if (appSettings.ObsOverlayShowQueue)
        {
            widgets.Add(new ObsLayoutWidget
            {
                Type = ObsWidgetType.Queue,
                X = 0.72, Y = 0.04, W = 0.26, H = 0.35,
                BgOpacity = appSettings.ObsOverlayBackgroundOpacity,
                Radius = appSettings.ObsOverlayCornerRadius
            });
        }

        return new ObsLayout { Widgets = widgets };
    }

    private byte[]? GetVisualizerBannerHtml(string vizId)
    {
        if (!redeemableVisualizers.TryGetInstalled(vizId, out var def))
            return null;

        // Check for obs_banner.html in binary assets
        if (def.HtmlContext?.BinaryAssets.TryGetValue("obs_banner.html", out var bannerBytes) == true)
            return bannerBytes;
        if (def.HtmlContext?.BinaryAssets.TryGetValue("obs_banner", out bannerBytes) == true)
            return bannerBytes;

        // Check for obs_banner.html in text assets
        if (def.HtmlContext?.TextAssets.TryGetValue("obs_banner.html", out var bannerText) == true)
            return System.Text.Encoding.UTF8.GetBytes(bannerText);

        return null;
    }

    private void PulseObs()
    {
        if (obsServer is null)
            return;

        var now = Environment.TickCount64;
        if (now < nextObsTick)
            return;

        nextObsTick = now + ObsIntervalMs;
        PushObsState();
    }

    private void PushObsState()
    {
        if (obsServer is null)
            return;

        var track = IsSpotifyActive ? spotifyCurrentTrack
            : IsSoundCloudActive ? soundCloudCurrentTrack
            : IsSunoActive ? sunoCurrentTrack
            : IsYouTubeActive ? youTubeCurrentTrack
            : engine.CurrentTrack;

        var isPlaying = IsSpotifyActive ? spotifyIsPlaying
            : IsSoundCloudActive ? soundCloudIsPlaying
            : IsSunoActive ? sunoIsPlaying
            : IsYouTubeActive ? youTubeIsPlaying
            : engine.IsPlaying;

        var position = IsSpotifyActive ? spotifyPositionSeconds
            : IsSoundCloudActive ? soundCloudPositionSeconds
            : IsSunoActive ? sunoPositionSeconds
            : IsYouTubeActive ? youTubePositionSeconds
            : engine.GetPosition();

        var length = IsSpotifyActive ? spotifyDurationSeconds
            : IsSoundCloudActive ? soundCloudDurationSeconds
            : IsSunoActive ? sunoDurationSeconds
            : IsYouTubeActive ? youTubeDurationSeconds
            : engine.GetLength();

        VisualizerFrame frame;
        if (IsYouTubeActive) frame = GetYouTubeVisualizerFrame();
        else if (IsSoundCloudActive) frame = GetSoundCloudVisualizerFrame();
        else if (IsSunoActive) frame = GetSunoVisualizerFrame();
        else if (IsSpotifyActive) frame = GetSpotifyVisualizerFrame();
        else frame = engine.GetVisualizerFrame();

        var lyrics = BuildObsLyrics(track, position);
        var queue = BuildObsQueue();
        var artworkVersion = track?.FilePath ?? "";

        var state = new ObsOverlayState
        {
            Track = new ObsTrackState
            {
                Title = track?.DisplayName ?? "",
                Artist = track?.Artist ?? "",
                Album = track?.Album ?? "",
                DurationSeconds = length,
                ArtworkVersion = artworkVersion
            },
            Playback = new ObsPlaybackState
            {
                IsPlaying = isPlaying,
                PositionSeconds = position,
                Volume = engine.Volume
            },
            Lyrics = lyrics,
            Queue = queue,
            Visualizer = new ObsVisualizerState
            {
                Levels = frame.Spectrum.Select(v => (double)v).ToArray(),
                Rms = frame.RmsLevel,
                Peak = frame.PeakLevel
            },
            Theme = new ObsThemeState
            {
                Accent = ColorToHex(AccentPrimaryColor),
                Background = ColorToHex(SurfaceBackColor),
                Foreground = ColorToHex(TextPrimaryColor)
            },
            SongWars = BuildObsSongWarsState(),
            LayoutSeq = obsServer.CurrentLayoutSeq
        };

        obsServer.UpdateState(state, track?.AlbumArtBytes, "image/jpeg");
    }

    private static ObsLyricsState BuildObsLyrics(AudioTrackInfo? track, double position)
    {
        var lyrics = track?.Lyrics;
        if (lyrics is null || !lyrics.HasLines || lyrics.IsDescription)
            return new ObsLyricsState();

        var idx = lyrics.FindLineIndex(position);
        if (idx < 0)
            return new ObsLyricsState();

        var current = lyrics.Lines[idx].Text;
        var next = idx + 1 < lyrics.Lines.Count ? lyrics.Lines[idx + 1].Text : "";
        return new ObsLyricsState { Current = current, Next = next };
    }

    private ObsQueueItem[] BuildObsQueue()
    {
        if (queue.IsEmpty)
            return [];

        return queue.Items
            .Select((path, i) =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return new ObsQueueItem
                {
                    Title = name,
                    Artist = "",
                    IsCurrent = i == queue.CurrentIndex
                };
            })
            .ToArray();
    }

    private void ShowObsDialog()
    {
        redeemableVisualizers.Reload();
        using var dialog = new ObsEditorDialog(appSettings, obsServer, redeemableVisualizers.Installed);
        dialog.ShowDialog(this);
        SaveAppSettings();
        ApplyObsSettings();
        // Signal connected browser sources to re-fetch the layout
        obsServer?.BumpLayoutVersion();
    }

    private void DisposeObs()
    {
        obsServer?.Dispose();
        obsServer = null;
        obsServerPort = 0;
        obsServerToken = "";
    }

    private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private ObsSongWarsState BuildObsSongWarsState()
    {
        var tournament = activeSongWarsDialog is { IsDisposed: false }
            ? activeSongWarsDialog.CurrentTournament
            : null;
        if (tournament is null)
            return new ObsSongWarsState();

        var current = SongWarsBracketEngine.GetCurrentMatch(tournament);
        var highlightId = activeSongWarsDialog?.CurrentOverlayMatchId ?? current?.MatchId ?? "";
        var highlightedMatch = tournament.Matches.FirstOrDefault(m =>
            string.Equals(m.MatchId, highlightId, StringComparison.OrdinalIgnoreCase));
        var focusMatch = highlightedMatch ?? current;

        var submissions = tournament.Submissions
            .Select(s => new ObsSongWarsSubmission
            {
                Id = s.SubmissionId,
                Title = s.DisplayTitle,
                Artist = s.ArtistDisplayName,
                Seed = s.Seed,
                Losses = s.Losses,
                Status = s.Status.ToString()
            })
            .ToArray();

        var winner = tournament.CurrentMatchId is null
            ? tournament.Submissions.FirstOrDefault(s => s.Status is SongWarsSubmissionStatus.Active or SongWarsSubmissionStatus.LosersBracket)
            : null;

        var currentIndex = string.IsNullOrWhiteSpace(current?.MatchId)
            ? -1
            : tournament.MatchOrder.IndexOf(current!.MatchId);
        var nextMatch = tournament.MatchOrder
            .Skip(Math.Max(0, currentIndex + 1))
            .Select(id => tournament.Matches.FirstOrDefault(m => string.Equals(m.MatchId, id, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(m => m is not null && m.Result == SongWarsOutcome.Pending);

        return new ObsSongWarsState
        {
            IsActive = true,
            TournamentId = tournament.TournamentId,
            Name = tournament.Name,
            CurrentMatchId = current?.MatchId ?? "",
            HighlightMatchId = highlightId,
            Phase = focusMatch is null ? "Complete" : FriendlySongWarsPhase(focusMatch.Phase),
            RoundLabel = focusMatch is null ? "Tournament Complete" : FriendlySongWarsRound(focusMatch),
            FocusSlot = focusMatch?.FocusSlot.ToString() ?? "",
            EliminationsUsed = tournament.EliminationCount,
            MaxEliminations = SongWarsVoteTally.MaxDirectEliminations,
            Submissions = submissions,
            Matches = tournament.Matches
                .OrderBy(m => m.Bracket)
                .ThenBy(m => m.RoundIndex)
                .ThenBy(m => tournament.MatchOrder.IndexOf(m.MatchId))
                .Select(m => ToObsSongWarsMatch(tournament, m, current?.MatchId, highlightId))
                .ToArray(),
            NextMatch = nextMatch is null ? null : ToObsSongWarsMatch(tournament, nextMatch, current?.MatchId, highlightId),
            Winner = winner is null ? null : new ObsSongWarsSubmission
            {
                Id = winner.SubmissionId,
                Title = winner.DisplayTitle,
                Artist = winner.ArtistDisplayName,
                Seed = winner.Seed,
                Losses = winner.Losses,
                Status = winner.Status.ToString()
            }
        };
    }

    private static ObsSongWarsMatch ToObsSongWarsMatch(
        SongWarsTournament tournament,
        SongWarsMatch match,
        string? currentMatchId,
        string? highlightMatchId)
    {
        var subA = FindSongWarsSubmission(tournament, match.SlotASubmissionId);
        var subB = FindSongWarsSubmission(tournament, match.SlotBSubmissionId);
        var winner = FindSongWarsSubmission(tournament, match.WinnerSubmissionId);
        var snapshot = match.VoteSnapshots.LastOrDefault();

        return new ObsSongWarsMatch
        {
            Id = match.MatchId,
            Bracket = match.Bracket.ToString(),
            RoundIndex = match.RoundIndex,
            RoundId = match.RoundId,
            RoundLabel = FriendlySongWarsRound(match),
            SlotAId = match.SlotASubmissionId,
            SlotBId = match.SlotBSubmissionId,
            SlotATitle = subA?.DisplayTitle ?? "Track A",
            SlotBTitle = subB?.DisplayTitle ?? "Track B",
            SlotAArtist = subA?.ArtistDisplayName ?? "",
            SlotBArtist = subB?.ArtistDisplayName ?? "",
            Phase = FriendlySongWarsPhase(match.Phase),
            Result = match.Result.ToString(),
            WinnerId = match.WinnerSubmissionId ?? "",
            WinnerTitle = winner?.DisplayTitle ?? "",
            EliminatedSlot = snapshot?.EliminatedSlot?.ToString() ?? "",
            IsCurrent = string.Equals(match.MatchId, currentMatchId, StringComparison.OrdinalIgnoreCase),
            IsHighlighted = string.Equals(match.MatchId, highlightMatchId, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static SongWarsSubmission? FindSongWarsSubmission(SongWarsTournament tournament, string? submissionId) =>
        string.IsNullOrWhiteSpace(submissionId)
            ? null
            : tournament.Submissions.FirstOrDefault(s =>
                string.Equals(s.SubmissionId, submissionId, StringComparison.OrdinalIgnoreCase));

    private static string FriendlySongWarsRound(SongWarsMatch match) =>
        match.Bracket switch
        {
            SongWarsBracket.Winners => $"Winners R{match.RoundIndex}",
            SongWarsBracket.Losers => $"Losers R{match.RoundIndex}",
            SongWarsBracket.GrandFinals => match.RoundIndex > 1 ? "Grand Reset" : "Grand Finals",
            _ => match.RoundId
        };

    private static string FriendlySongWarsPhase(SongWarsMatchPhase phase) => phase switch
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
        _ => phase.ToString()
    };

    private static string ToObsOverlaySlug(string value)
    {
        var chars = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-')
            .ToArray();
        var slug = string.Join("-", new string(chars)
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return string.IsNullOrWhiteSpace(slug) ? "default" : slug;
    }
}
