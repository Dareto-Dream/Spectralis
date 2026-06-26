using System.Drawing;
using System.IO;

namespace Spectralis;

// Batch tag editor — only writes fields the user explicitly changes.
// Fields with differing values across all selected files show "(multiple values)" as placeholder.
internal sealed class BatchTagEditorDialog : Form
{
    private const string MixedPlaceholder = "(multiple values)";

    private readonly List<TagEditorModel> _models;
    private readonly HashSet<string>      _changed = [];

    private readonly TextBox        txtTitle       = new();
    private readonly TextBox        txtArtist      = new();
    private readonly TextBox        txtAlbumArtist = new();
    private readonly TextBox        txtAlbum       = new();
    private readonly NumericUpDown  numYear        = new();
    private readonly NumericUpDown  numBpm         = new();
    private readonly TextBox        txtGenre       = new();
    private readonly TextBox        txtComposer    = new();
    private readonly TextBox        txtComment     = new();

    private readonly Button  btnSave   = new();
    private readonly Button  btnCancel = new();
    private readonly Label   lblStatus = new();

    public BatchTagEditorDialog(string[] paths, ThemePalette theme)
    {
        _models = paths.Select(TagEditorService.Read).ToList();

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = $"Edit Tags — {paths.Length} tracks";
        Size                = new Size(520, 450);
        MinimumSize         = new Size(480, 400);
        FormBorderStyle     = FormBorderStyle.Sizable;
        MaximizeBox         = false;
        StartPosition       = FormStartPosition.CenterParent;

        BuildLayout(theme);
        PopulateFields();
        ApplyTheme(theme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout(ThemePalette theme)
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 2,
            Padding     = new Padding(14, 12, 14, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(BuildFieldsPanel(), 0, 0);

        var bottomBar = BuildBottomBar(theme);
        root.Controls.Add(bottomBar, 0, 1);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private Panel BuildFieldsPanel()
    {
        var grid = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void ConfigNum(NumericUpDown n, decimal max)
        {
            n.Minimum       = 0;
            n.Maximum       = max;
            n.DecimalPlaces = 0;
            n.Font          = new Font("Segoe UI", 9.5f);
        }
        ConfigNum(numYear, 9999);
        ConfigNum(numBpm, 999);

        void AddFieldRow(string labelText, Control ctrl)
        {
            var lbl = new Label
            {
                Text      = labelText,
                AutoSize  = false,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding   = new Padding(0, 0, 6, 0),
                Font      = new Font("Segoe UI", 9f),
            };
            ctrl.Dock = DockStyle.Fill;
            if (ctrl is TextBox tb) tb.Font = new Font("Segoe UI", 9.5f);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.Controls.Add(lbl);
            grid.Controls.Add(ctrl);
        }

        void AddPairRow(string labelText, NumericUpDown first, string midLabel, NumericUpDown second)
        {
            var lbl   = new Label
            {
                Text      = labelText,
                AutoSize  = false,
                Dock      = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight,
                Padding   = new Padding(0, 0, 6, 0),
                Font      = new Font("Segoe UI", 9f),
            };
            var panel = new Panel { Dock = DockStyle.Fill };
            first.Dock     = DockStyle.None;
            first.Location = new Point(0, 3);
            first.Width    = 72;
            var midLbl = new Label
            {
                Text     = midLabel,
                AutoSize = true,
                Location = new Point(78, 6),
                Font     = new Font("Segoe UI", 9f),
            };
            second.Dock     = DockStyle.None;
            second.Location = new Point(78 + midLbl.PreferredWidth + 6, 3);
            second.Width    = 72;
            panel.Controls.AddRange([first, midLbl, second]);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.Controls.Add(lbl);
            grid.Controls.Add(panel);
        }

        AddFieldRow("Title",       txtTitle);
        AddFieldRow("Artist",      txtArtist);
        AddFieldRow("Alb. Artist", txtAlbumArtist);
        AddFieldRow("Album",       txtAlbum);
        AddPairRow("Year", numYear, "BPM", numBpm);
        AddFieldRow("Genre",    txtGenre);
        AddFieldRow("Composer", txtComposer);

        var commentLbl = new Label
        {
            Text      = "Comment",
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.TopRight,
            Padding   = new Padding(0, 6, 6, 0),
            Font      = new Font("Segoe UI", 9f),
        };
        txtComment.Multiline  = true;
        txtComment.ScrollBars = ScrollBars.Vertical;
        txtComment.Dock       = DockStyle.Fill;
        txtComment.Font       = new Font("Segoe UI", 9.5f);
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(commentLbl);
        grid.Controls.Add(txtComment);

        grid.RowCount = grid.RowStyles.Count;
        return grid;
    }

    private Panel BuildBottomBar(ThemePalette theme)
    {
        var bar = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 3,
            RowCount    = 1,
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblStatus.AutoSize  = false;
        lblStatus.Dock      = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Font      = new Font("Segoe UI", 8.5f);
        lblStatus.ForeColor = theme.TextMutedColor;

        void StyleBtn(Button b, bool primary = false)
        {
            b.Size      = new Size(80, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            b.Anchor    = AnchorStyles.None;
            if (primary)
            {
                b.BackColor = theme.AccentPrimaryColor;
                b.ForeColor = theme.AccentContrastColor;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = theme.SurfaceRaisedColor;
                b.ForeColor = theme.TextSecondaryColor;
                b.FlatAppearance.BorderColor = theme.BorderStrongColor;
            }
        }

        btnSave.Text   = "Save All";
        btnCancel.Text = "Cancel";
        StyleBtn(btnSave, primary: true);
        StyleBtn(btnCancel);

        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        bar.Controls.Add(lblStatus,  0, 0);
        bar.Controls.Add(btnSave,    1, 0);
        bar.Controls.Add(btnCancel,  2, 0);
        return bar;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Population + change tracking
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateFields()
    {
        void SetText(TextBox tb, string fieldName, Func<TagEditorModel, string?> get)
        {
            var distinct = _models.Select(get).Distinct().ToList();
            if (distinct.Count == 1)
            {
                tb.Text = distinct[0] ?? "";
            }
            else
            {
                tb.Text            = "";
                tb.PlaceholderText = MixedPlaceholder;
            }
            tb.TextChanged += (_, _) =>
            {
                if (tb.Text != "" || tb.Focused)
                    _changed.Add(fieldName);
            };
        }

        void SetNum(NumericUpDown nud, string fieldName, Func<TagEditorModel, uint> get)
        {
            var distinct = _models.Select(get).Distinct().ToList();
            nud.Value = distinct.Count == 1 ? Math.Min(distinct[0], nud.Maximum) : 0;
            nud.ValueChanged += (_, _) => _changed.Add(fieldName);
        }

        SetText(txtTitle,       "Title",       m => m.Title);
        SetText(txtArtist,      "Artist",      m => m.Artist);
        SetText(txtAlbumArtist, "AlbumArtist", m => m.AlbumArtist);
        SetText(txtAlbum,       "Album",       m => m.Album);
        SetText(txtGenre,       "Genre",       m => m.Genre);
        SetText(txtComposer,    "Composer",    m => m.Composer);
        SetText(txtComment,     "Comment",     m => m.Comment);
        SetNum(numYear,  "Year", m => m.Year);
        SetNum(numBpm,   "BPM",  m => m.BPM);
    }

    private void ApplyTheme(ThemePalette theme)
    {
        BackColor = theme.WindowBackColor;
        ForeColor = theme.TextPrimaryColor;

        foreach (var ctrl in AllDescendants(this))
        {
            switch (ctrl)
            {
                case Label lbl:
                    lbl.ForeColor = theme.TextSecondaryColor;
                    lbl.BackColor = Color.Transparent;
                    break;
                case TextBox tb:
                    tb.BackColor   = theme.SurfaceRaisedColor;
                    tb.ForeColor   = theme.TextPrimaryColor;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = theme.SurfaceRaisedColor;
                    nud.ForeColor = theme.TextPrimaryColor;
                    break;
            }
        }
    }

    private static IEnumerable<Control> AllDescendants(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var d in AllDescendants(c))
                yield return d;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Save
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (_changed.Count == 0)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        var errors = 0;
        foreach (var model in _models)
        {
            if (_changed.Contains("Title"))       model.Title       = Trim(txtTitle.Text);
            if (_changed.Contains("Artist"))      model.Artist      = Trim(txtArtist.Text);
            if (_changed.Contains("AlbumArtist")) model.AlbumArtist = Trim(txtAlbumArtist.Text);
            if (_changed.Contains("Album"))       model.Album       = Trim(txtAlbum.Text);
            if (_changed.Contains("Genre"))       model.Genre       = Trim(txtGenre.Text);
            if (_changed.Contains("Composer"))    model.Composer    = Trim(txtComposer.Text);
            if (_changed.Contains("Comment"))     model.Comment     = Trim(txtComment.Text);
            if (_changed.Contains("Year"))        model.Year        = (uint)numYear.Value;
            if (_changed.Contains("BPM"))         model.BPM         = (uint)numBpm.Value;

            try { TagEditorService.Write(model); }
            catch { errors++; }
        }

        if (errors > 0)
            lblStatus.Text = $"{errors} file(s) could not be saved.";
        else
        {
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    private static string? Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
