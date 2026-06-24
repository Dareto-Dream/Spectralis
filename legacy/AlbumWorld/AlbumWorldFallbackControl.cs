using System.Drawing;

namespace Spectralis;

internal sealed class AlbumWorldFallbackControl : Control
{
    private readonly ListBox trackList;
    private readonly Label lblAlbumTitle;
    private readonly Label lblAlbumArtist;
    private List<AlbumTrackEntry> tracks = [];

    public event EventHandler<string>? TrackSelected;

    public AlbumWorldFallbackControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);

        lblAlbumTitle = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 36,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold),
            Padding = new Padding(12, 0, 0, 0)
        };

        lblAlbumArtist = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10f),
            Padding = new Padding(14, 0, 0, 0)
        };

        trackList = new ListBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = new Font("Segoe UI", 11f),
            SelectionMode = SelectionMode.One,
            IntegralHeight = false
        };

        trackList.DoubleClick += TrackList_DoubleClick;
        trackList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
                FireSelectedTrack();
        };

        Controls.Add(trackList);
        Controls.Add(lblAlbumArtist);
        Controls.Add(lblAlbumTitle);
    }

    public void LoadAlbum(AlbumManifest manifest, AlbumWorldSession session)
    {
        tracks = manifest.Tracks;
        lblAlbumTitle.Text = manifest.Title;
        lblAlbumArtist.Text = manifest.Artist;

        trackList.Items.Clear();
        var trackIndex = 1;
        foreach (var track in tracks)
        {
            var completed = session.TrackStats.TryGetValue(track.Id, out var stats) && stats.Completed;
            var prefix = completed ? "✓ " : "  ";
            trackList.Items.Add($"{prefix}{trackIndex++}. {track.Title}");
        }
    }

    public void UpdateSession(AlbumWorldSession session)
    {
        for (var i = 0; i < tracks.Count && i < trackList.Items.Count; i++)
        {
            var track = tracks[i];
            var completed = session.TrackStats.TryGetValue(track.Id, out var stats) && stats.Completed;
            var prefix = completed ? "✓ " : "  ";
            trackList.Items[i] = $"{prefix}{i + 1}. {track.Title}";
        }
    }

    public void HighlightTrack(string trackId)
    {
        var idx = tracks.FindIndex(t => t.Id == trackId);
        if (idx >= 0 && idx < trackList.Items.Count)
            trackList.SelectedIndex = idx;
    }

    public void Clear()
    {
        tracks = [];
        trackList.Items.Clear();
        lblAlbumTitle.Text = "";
        lblAlbumArtist.Text = "";
    }

    public void ApplyTheme(ThemePalette palette)
    {
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        lblAlbumTitle.BackColor = palette.WindowBackColor;
        lblAlbumTitle.ForeColor = palette.TextPrimaryColor;
        lblAlbumArtist.BackColor = palette.WindowBackColor;
        lblAlbumArtist.ForeColor = palette.TextSecondaryColor;
        trackList.BackColor = palette.SurfaceBackColor;
        trackList.ForeColor = palette.TextPrimaryColor;
    }

    private void TrackList_DoubleClick(object? sender, EventArgs e)
    {
        FireSelectedTrack();
    }

    private void FireSelectedTrack()
    {
        var idx = trackList.SelectedIndex;
        if (idx < 0 || idx >= tracks.Count)
            return;

        TrackSelected?.Invoke(this, tracks[idx].Id);
    }
}
