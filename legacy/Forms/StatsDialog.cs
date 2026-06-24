using System.Drawing;

namespace Spectralis;

internal sealed class StatsDialog : Form
{
    private readonly ThemePalette _theme;

    private readonly ModernComboBox cmbPeriod   = new();
    private readonly Label        lblScrobbles = new();
    private readonly Label        lblHours     = new();
    private readonly Label        lblStreak    = new();
    private readonly ListView     lstArtists   = new();
    private readonly ListView     lstTracks    = new();
    private readonly Button       btnClose     = new();

    public StatsDialog(ThemePalette theme)
    {
        _theme              = theme;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "My Listening";
        FormBorderStyle     = FormBorderStyle.FixedDialog;
        StartPosition       = FormStartPosition.CenterParent;
        MaximizeBox         = false;
        MinimizeBox         = false;
        ClientSize          = new Size(560, 520);

        BuildLayout();
        ApplyTheme();
        cmbPeriod.SelectedIndex = 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock      = DockStyle.Fill,
            RowCount  = 4,
            ColumnCount = 1,
            Margin    = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));  // period filter
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));  // summary stats
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // lists
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // bottom bar
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildFilterBar(), 0, 0);
        root.Controls.Add(BuildSummaryPanel(), 0, 1);
        root.Controls.Add(BuildListsPanel(), 0, 2);
        root.Controls.Add(BuildBottomBar(), 0, 3);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private Panel BuildFilterBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 4) };

        var lbl = new Label
        {
            Text      = "Show:",
            AutoSize  = true,
            Dock      = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        lbl.Font = new Font("Segoe UI", 9f);

        cmbPeriod.Items.AddRange(["This Week", "This Month", "All Time"]);
        cmbPeriod.Dock          = DockStyle.Left;
        cmbPeriod.Width         = 120;
        cmbPeriod.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbPeriod.Font          = new Font("Segoe UI", 9f);
        cmbPeriod.SelectedIndexChanged += (_, _) => Reload();

        var spacer = new Panel { Dock = DockStyle.Left, Width = 6 };
        panel.Controls.Add(cmbPeriod);
        panel.Controls.Add(spacer);
        panel.Controls.Add(lbl);
        return panel;
    }

    private Panel BuildSummaryPanel()
    {
        var panel = new Panel
        {
            Dock    = DockStyle.Fill,
            Padding = new Padding(10, 4, 10, 4),
        };

        var font = new Font("Segoe UI", 9f);

        lblScrobbles.Text      = "— scrobbles";
        lblScrobbles.AutoSize  = true;
        lblScrobbles.Dock      = DockStyle.Left;
        lblScrobbles.TextAlign = ContentAlignment.MiddleCenter;
        lblScrobbles.Padding   = new Padding(0, 0, 20, 0);
        lblScrobbles.Font      = font;

        lblHours.Text      = "— hours";
        lblHours.AutoSize  = true;
        lblHours.Dock      = DockStyle.Left;
        lblHours.TextAlign = ContentAlignment.MiddleCenter;
        lblHours.Padding   = new Padding(0, 0, 20, 0);
        lblHours.Font      = font;

        lblStreak.Text      = "— day streak";
        lblStreak.AutoSize  = true;
        lblStreak.Dock      = DockStyle.Left;
        lblStreak.TextAlign = ContentAlignment.MiddleCenter;
        lblStreak.Font      = font;

        panel.Controls.Add(lblStreak);
        panel.Controls.Add(lblHours);
        panel.Controls.Add(lblScrobbles);
        return panel;
    }

    private TableLayoutPanel BuildListsPanel()
    {
        var tbl = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            RowCount    = 1,
            ColumnCount = 2,
            Margin      = Padding.Empty,
        };
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        tbl.Controls.Add(BuildList(lstArtists, "Top Artists", ["Artist", "Plays"]), 0, 0);
        tbl.Controls.Add(BuildList(lstTracks,  "Top Tracks",  ["Title", "Artist", "Plays"]), 1, 0);
        return tbl;
    }

    private static Panel BuildList(ListView lv, string title, string[] columns)
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(6) };

        var hdr = new Label
        {
            Text      = title,
            Dock      = DockStyle.Top,
            Height    = 22,
            Font      = new Font("Segoe UI Semibold", 9f),
            TextAlign = ContentAlignment.MiddleLeft,
        };

        lv.Dock          = DockStyle.Fill;
        lv.View          = View.Details;
        lv.FullRowSelect = true;
        lv.MultiSelect   = false;
        lv.BorderStyle   = BorderStyle.None;
        lv.HeaderStyle   = ColumnHeaderStyle.Nonclickable;
        lv.Font          = new Font("Segoe UI", 9f);

        if (columns.Length == 2)
        {
            lv.Columns.Add(columns[0], -2);
            lv.Columns.Add(columns[1], 50);
        }
        else
        {
            lv.Columns.Add(columns[0], 120);
            lv.Columns.Add(columns[1], 80);
            lv.Columns.Add(columns[2], 44);
        }

        panel.Controls.Add(lv);
        panel.Controls.Add(hdr);
        return panel;
    }

    private Panel BuildBottomBar()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };

        btnClose.Text      = "Close";
        btnClose.Width     = 80;
        btnClose.Dock      = DockStyle.Right;
        btnClose.FlatStyle = FlatStyle.Flat;
        btnClose.Click    += (_, _) => Close();

        panel.Controls.Add(btnClose);
        return panel;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Data
    // ─────────────────────────────────────────────────────────────────────────

    private void Reload()
    {
        var history = ScrobbleQueue.LoadHistory();
        var since   = cmbPeriod.SelectedIndex switch
        {
            0 => DateTime.UtcNow.Date.AddDays(-6),
            1 => DateTime.UtcNow.Date.AddDays(-29),
            _ => DateTime.MinValue,
        };

        var stats = ListeningStats.Compute(history, since);

        lblScrobbles.Text = $"{stats.TotalScrobbles:N0} scrobbles";
        lblHours.Text     = $"{stats.TotalHours:F1} hours";
        lblStreak.Text    = stats.CurrentStreakDays == 1
            ? "1 day streak"
            : $"{stats.CurrentStreakDays} day streak";

        lstArtists.BeginUpdate();
        lstArtists.Items.Clear();
        foreach (var a in stats.TopArtists)
        {
            var item = new ListViewItem(a.Artist);
            item.SubItems.Add(a.Plays.ToString());
            lstArtists.Items.Add(item);
        }
        lstArtists.EndUpdate();

        lstTracks.BeginUpdate();
        lstTracks.Items.Clear();
        foreach (var t in stats.TopTracks)
        {
            var item = new ListViewItem(t.Title);
            item.SubItems.Add(t.Artist);
            item.SubItems.Add(t.Plays.ToString());
            lstTracks.Items.Add(item);
        }
        lstTracks.EndUpdate();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Theme
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;

        lstArtists.BackColor = _theme.WindowBackColor;
        lstArtists.ForeColor = _theme.TextPrimaryColor;
        lstTracks.BackColor  = _theme.WindowBackColor;
        lstTracks.ForeColor  = _theme.TextPrimaryColor;

        foreach (var c in GetAllChildren(this))
        {
            switch (c)
            {
                case Button btn:
                    btn.BackColor = _theme.SurfaceRaisedColor;
                    btn.ForeColor = _theme.TextSecondaryColor;
                    btn.FlatAppearance.BorderColor = _theme.BorderStrongColor;
                    break;
                case ModernComboBox cb:
                    ThemeControlStyler.ApplyComboBoxTheme(cb, _theme);
                    break;
                case Label lbl when lbl != lblScrobbles && lbl != lblHours && lbl != lblStreak:
                    lbl.ForeColor = _theme.TextSecondaryColor;
                    break;
            }
        }
    }

    private static IEnumerable<Control> GetAllChildren(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in GetAllChildren(c))
                yield return child;
        }
    }
}
