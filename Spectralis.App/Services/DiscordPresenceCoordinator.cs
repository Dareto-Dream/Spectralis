using Avalonia.Threading;
using Spectralis.Core.Audio;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Services;

/// <summary>
/// Feeds the Discord Rich Presence service from the engine once a second. The
/// service self-deduplicates via presence signatures, silently retries IPC
/// initialization, and stays inert when no application ID is configured.
/// Also listens to Spotify playback state to show Spotify tracks on Discord.
/// </summary>
public sealed class DiscordPresenceCoordinator : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Func<ListeningActivitySnapshot> _getIdleActivity;
    private readonly DiscordRichPresenceService _service = new();
    private readonly DispatcherTimer _timer;
    private SpotifyTrackState? _currentSpotifyTrack;
    private bool _spotifyIsPlaying;

    /// <summary>Set by Shared Play hosting; appears as the Listen Together button.</summary>
    public string? SharedPlayJoinUrl { get; set; }

    public DiscordPresenceCoordinator(AudioEngine engine, Func<ListeningActivitySnapshot>? getIdleActivity = null)
    {
        _engine = engine;
        _getIdleActivity = getIdleActivity ?? (() => ListeningActivitySnapshot.Empty);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Push());
        _timer.Start();
    }

    public void SetSpotifyHost(SpotifyPlaybackHostService? spotifyHost)
    {
        if (spotifyHost is not null)
        {
            spotifyHost.TrackStateChanged += OnSpotifyTrackStateChanged;
        }
    }

    public void SetEnabled(bool enabled) => _service.SetEnabled(enabled);

    private void OnSpotifyTrackStateChanged(object? sender, SpotifyTrackState state)
    {
        _currentSpotifyTrack = state;
        _spotifyIsPlaying = !state.IsPaused;
    }

    private void Push()
    {
        if (_spotifyIsPlaying && _currentSpotifyTrack is not null)
        {
            var spotifyTrack = BuildSpotifyTrackInfo(_currentSpotifyTrack);
            _service.Update(
                spotifyTrack,
                !_currentSpotifyTrack.IsPaused,
                TimeSpan.FromMilliseconds(_currentSpotifyTrack.PositionMs),
                TimeSpan.FromMilliseconds(_currentSpotifyTrack.DurationMs),
                SharedPlayJoinUrl,
                idleActivity: _getIdleActivity());
        }
        else
        {
            _service.Update(
                _engine.CurrentTrack,
                _engine.IsPlaying,
                TimeSpan.FromSeconds(_engine.GetPosition()),
                TimeSpan.FromSeconds(_engine.GetLength()),
                SharedPlayJoinUrl,
                idleActivity: _getIdleActivity());
        }
    }

    private static TrackInfo BuildSpotifyTrackInfo(SpotifyTrackState state) =>
        new()
        {
            SourcePath = $"spotify:{state.TrackId}",
            Title = state.Name,
            Artist = state.Artist,
            Album = state.Album,
            Duration = TimeSpan.FromMilliseconds(state.DurationMs)
        };

    public void Dispose()
    {
        _timer.Stop();
        _service.Dispose();
    }
}
