#if WINDOWS10_0_19041_0_OR_GREATER
using Spectralis.App.Services;
using Spectralis.Core.Platform;
using Windows.Media;
using Windows.Storage.Streams;

namespace Spectralis.App.Platform.Windows;

/// <summary>
/// Windows SMTC integration: OS-level media keys/Now Playing flyout with
/// metadata, artwork, play/pause/stop/next/previous, and timeline seek.
/// Ported from the legacy WindowsNowPlayingService.
/// </summary>
public sealed class WindowsMediaSessionService : IMediaSessionService
{
    private SystemMediaTransportControls? _controls;
    private InMemoryRandomAccessStream? _artworkStream;
    private TimeSpan _lastTimelinePosition = TimeSpan.MinValue;
    private TimeSpan _lastTimelineDuration = TimeSpan.MinValue;
    private bool _lastIsPlaying;
    private bool _hasMetadata;

    public event EventHandler? PlayRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PreviousRequested;
    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>Raised for the SMTC stop button (beyond the shared interface).</summary>
    public event EventHandler? StopRequested;

    public WindowsMediaSessionService(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        try
        {
            _controls = SystemMediaTransportControlsInterop.GetForWindow(windowHandle);
            _controls.ButtonPressed += OnButtonPressed;
            _controls.PlaybackPositionChangeRequested += OnPositionChangeRequested;
            _controls.IsEnabled = false;
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("Failed to initialize SMTC.", ex);
            _controls = null;
        }
    }

    public void UpdateMetadata(string title, string artist, string album, byte[]? artwork)
    {
        if (_controls is null)
        {
            return;
        }

        try
        {
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            {
                Clear();
                return;
            }

            var updater = _controls.DisplayUpdater;
            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = title;
            updater.MusicProperties.Artist = artist;
            updater.MusicProperties.AlbumTitle = album;
            updater.Thumbnail = CreateThumbnail(artwork);
            updater.Update();

            _controls.IsEnabled = true;
            _controls.IsPlayEnabled = true;
            _controls.IsPauseEnabled = true;
            _controls.IsStopEnabled = true;
            _controls.IsNextEnabled = true;
            _controls.IsPreviousEnabled = true;
            _hasMetadata = true;
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("SMTC metadata update failed.", ex);
        }
    }

    public void UpdatePlaybackState(bool isPlaying, TimeSpan position, TimeSpan duration)
    {
        if (_controls is null || !_hasMetadata)
        {
            return;
        }

        try
        {
            if (isPlaying != _lastIsPlaying)
            {
                _controls.PlaybackStatus = isPlaying
                    ? MediaPlaybackStatus.Playing
                    : MediaPlaybackStatus.Paused;
                _lastIsPlaying = isPlaying;
            }

            // Timeline pushes are rate-limited: only when duration changes or the
            // position drifts more than a second from the last published value.
            if (duration != _lastTimelineDuration ||
                (position - _lastTimelinePosition).Duration() > TimeSpan.FromSeconds(1))
            {
                _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
                {
                    StartTime = TimeSpan.Zero,
                    MinSeekTime = TimeSpan.Zero,
                    Position = position,
                    MaxSeekTime = duration,
                    EndTime = duration,
                });
                _lastTimelinePosition = position;
                _lastTimelineDuration = duration;
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Error("SMTC playback state update failed.", ex);
        }
    }

    private void Clear()
    {
        if (_controls is null)
        {
            return;
        }

        _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
        _controls.DisplayUpdater.ClearAll();
        _controls.IsEnabled = false;
        _hasMetadata = false;
        _lastIsPlaying = false;
        _lastTimelinePosition = TimeSpan.MinValue;
        _lastTimelineDuration = TimeSpan.MinValue;
        DisposeArtworkStream();
    }

    private RandomAccessStreamReference? CreateThumbnail(byte[]? albumArtBytes)
    {
        DisposeArtworkStream();
        if (albumArtBytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            _artworkStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(_artworkStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(albumArtBytes);
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            }

            return RandomAccessStreamReference.CreateFromStream(_artworkStream);
        }
        catch
        {
            return null;
        }
    }

    private void DisposeArtworkStream()
    {
        _artworkStream?.Dispose();
        _artworkStream = null;
    }

    private void OnButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Play:
                PlayRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Pause:
                PauseRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Stop:
                StopRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Next:
                NextRequested?.Invoke(this, EventArgs.Empty);
                break;
            case SystemMediaTransportControlsButton.Previous:
                PreviousRequested?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private void OnPositionChangeRequested(
        SystemMediaTransportControls sender,
        PlaybackPositionChangeRequestedEventArgs args)
    {
        SeekRequested?.Invoke(this, args.RequestedPlaybackPosition);
    }

    public void Dispose()
    {
        if (_controls is not null)
        {
            try
            {
                _controls.ButtonPressed -= OnButtonPressed;
                _controls.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
                Clear();
            }
            catch
            {
            }

            _controls = null;
        }

        DisposeArtworkStream();
    }
}
#endif
