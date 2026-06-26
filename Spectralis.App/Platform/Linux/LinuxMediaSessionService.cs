using Spectralis.Core.Platform;

namespace Spectralis.App.Platform.Linux;

/// <summary>
/// MPRIS (org.mpris.MediaPlayer2) implementation skeleton for Linux. Compiles
/// against IMediaSessionService per the BLOCKERS decision; the DBus export
/// (bus name org.mpris.MediaPlayer2.spectralis, object path
/// /org/mpris/MediaPlayer2, interfaces MediaPlayer2 + MediaPlayer2.Player)
/// lands with a Tmds.DBus binding once exercised under WSLg.
/// </summary>
public sealed class LinuxMediaSessionService : IMediaSessionService
{
    public const string BusName = "org.mpris.MediaPlayer2.spectralis";
    public const string ObjectPath = "/org/mpris/MediaPlayer2";

    // Latest pushed state, held for the DBus property surface.
    private string _title = string.Empty;
    private string _artist = string.Empty;
    private string _album = string.Empty;
    private bool _isPlaying;
    private TimeSpan _position;
    private TimeSpan _duration;

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler<TimeSpan>? SeekRequested;

    public void UpdateMetadata(string title, string artist, string album, byte[]? artwork)
    {
        _title = title;
        _artist = artist;
        _album = album;
        // DBus: emit PropertiesChanged for Metadata (xesam:title/artist/album, mpris:artUrl).
    }

    public void UpdatePlaybackState(bool isPlaying, TimeSpan position, TimeSpan duration)
    {
        _isPlaying = isPlaying;
        _position = position;
        _duration = duration;
        // DBus: emit PropertiesChanged for PlaybackStatus + Position/mpris:length.
    }

    /// <summary>Remote-control entry points the DBus method handlers will call.</summary>
    internal void RaisePlay() => PlayRequested?.Invoke(this, EventArgs.Empty);
    internal void RaisePause() => PauseRequested?.Invoke(this, EventArgs.Empty);
    internal void RaiseNext() => NextRequested?.Invoke(this, EventArgs.Empty);
    internal void RaisePrevious() => PreviousRequested?.Invoke(this, EventArgs.Empty);
    internal void RaiseSeek(TimeSpan position) => SeekRequested?.Invoke(this, position);

    internal (string Title, string Artist, string Album, bool IsPlaying, TimeSpan Position, TimeSpan Duration) CurrentState =>
        (_title, _artist, _album, _isPlaying, _position, _duration);

    public void Dispose()
    {
        // DBus connection teardown once the binding lands.
    }
}
