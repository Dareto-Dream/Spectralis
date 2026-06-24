namespace Spectralis;

public partial class Form1
{
    private ToolStripMenuItem? fileLyricsTimingStudioToolStripMenuItem;

    private void InitializeLyricsTimingStudioMenu()
    {
        fileLyricsTimingStudioToolStripMenuItem = new ToolStripMenuItem
        {
            Name = "fileLyricsTimingStudioToolStripMenuItem",
            ShortcutKeys = Keys.Control | Keys.Shift | Keys.L,
            Text = "Lyrics &Timing Studio..."
        };
        fileLyricsTimingStudioToolStripMenuItem.Click += (_, _) => ShowLyricsTimingStudio();

        toolsToolStripMenuItem.DropDownItems.Add(fileLyricsTimingStudioToolStripMenuItem);
    }

    private void ShowLyricsTimingStudio()
    {
        using var dialog = new LyricsTimingStudioDialog(
            GetActiveTrackForUi(),
            GetActivePlaybackPositionSeconds,
            IsActivePlaybackRunning,
            ToggleActivePlayback,
            SeekActivePlayback,
            themePalette);
        dialog.ShowDialog(this);
    }

    private double GetActivePlaybackPositionSeconds() =>
        IsSpotifyActive ? SpotifyCurrentPositionSeconds
        : IsSoundCloudActive ? soundCloudPositionSeconds
        : IsSunoActive ? sunoPositionSeconds
        : IsYouTubeActive ? youTubePositionSeconds
        : engine.GetPosition();

    private bool IsActivePlaybackRunning() =>
        IsSpotifyActive ? spotifyIsPlaying
        : IsSoundCloudActive ? soundCloudIsPlaying
        : IsSunoActive ? sunoIsPlaying
        : IsYouTubeActive ? youTubeIsPlaying
        : engine.IsPlaying;

    private void ToggleActivePlayback() => btnPlayPause_Click(this, EventArgs.Empty);

    private void SeekActivePlayback(double seconds)
    {
        var clamped = (float)Math.Max(0, seconds);
        if (IsSpotifyActive)
        {
            _ = SpotifySeekAsync(clamped);
            return;
        }

        if (IsSoundCloudActive)
        {
            _ = SoundCloudSeekAsync(clamped);
            return;
        }

        if (IsSunoActive)
        {
            _ = SunoSeekAsync(clamped);
            return;
        }

        if (IsYouTubeActive)
        {
            _ = YouTubeSeekAsync(clamped);
            return;
        }

        engine.Seek(clamped);
        SeekReactive(clamped);
        NotifySharedPlayPlaybackChanged("seek");
        UpdateUiState();
    }
}
