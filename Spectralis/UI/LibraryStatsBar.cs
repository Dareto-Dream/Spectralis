using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class LibraryStatsBar : StatusStrip
    {
        private readonly ToolStripStatusLabel _lblTracks;
        private readonly ToolStripStatusLabel _lblDuration;
        private readonly ToolStripStatusLabel _lblArtists;

        public LibraryStatsBar()
        {
            BackColor = Color.FromArgb(25, 25, 25);
            SizingGrip = false;

            _lblTracks = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(160, 160, 160),
                Spring = false
            };
            _lblDuration = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(120, 120, 120),
                Spring = true,
                TextAlign = ContentAlignment.MiddleCenter
            };
            _lblArtists = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(120, 120, 120),
                Spring = false
            };

            Items.AddRange(new ToolStripItem[] { _lblTracks, _lblDuration, _lblArtists });
        }

        public void Update(LibraryStats stats)
        {
            _lblTracks.Text = $"{stats.TotalTracks:N0} tracks";
            _lblDuration.Text = FormatTotalDuration(stats.TotalDurationMs);
            _lblArtists.Text = $"{stats.UniqueArtists} artists";
        }

        public void Clear()
        {
            _lblTracks.Text = "No library";
            _lblDuration.Text = string.Empty;
            _lblArtists.Text = string.Empty;
        }

        private static string FormatTotalDuration(long ms)
        {
            var ts = TimeSpan.FromMilliseconds(ms);
            if (ts.TotalHours >= 1)
                return $"{(int)ts.TotalHours}h {ts.Minutes}m";
            return $"{ts.Minutes}m {ts.Seconds}s";
        }
    }
}
