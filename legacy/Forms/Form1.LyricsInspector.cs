namespace Spectralis;

public partial class Form1
{
    private void btnInspectLyrics_Click(object sender, EventArgs e)
    {
        var track = GetActiveTrackForUi();
        if (!IsAppLyricsAvailable(track) || track?.Lyrics is not { HasLines: true })
        {
            return;
        }

        using var dialog = new LyricsInspectorDialog(track, themePalette, GetActiveLyricPositionSeconds());
        dialog.FitToOwnerClient(this);
        dialog.ShowDialog(this);
    }

    private double GetActiveLyricPositionSeconds() =>
        IsSpotifyActive ? SpotifyCurrentPositionSeconds
        : IsSoundCloudActive ? soundCloudPositionSeconds
        : IsSunoActive ? sunoPositionSeconds
        : IsYouTubeActive ? youTubePositionSeconds
        : engine.GetPosition();
}
