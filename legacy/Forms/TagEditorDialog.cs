using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Spectralis;

internal sealed class TagEditorDialog : Form
{
    private TagEditorModel _model;
    private readonly ThemePalette _theme;

    // Cover art
    private readonly PictureBox     picCover       = new();
    private readonly Button         btnChangeCover = new();
    private readonly Button         btnRemoveCover = new();

    // Tag fields
    private readonly TextBox        txtTitle       = new();
    private readonly TextBox        txtArtist      = new();
    private readonly TextBox        txtAlbumArtist = new();
    private readonly TextBox        txtAlbum       = new();
    private readonly NumericUpDown  numTrack       = new();
    private readonly NumericUpDown  numDisc        = new();
    private readonly NumericUpDown  numYear        = new();
    private readonly NumericUpDown  numBpm         = new();
    private readonly TextBox        txtGenre       = new();
    private readonly TextBox        txtComposer    = new();
    private readonly TextBox        txtComment     = new();

    // Bottom bar
    private readonly Button         btnFetchMb     = new();
    private readonly Button         btnRevert      = new();
    private readonly Button         btnSave        = new();
    private readonly Button         btnCancel      = new();
    private readonly Label          lblStatus      = new();

    public TagEditorDialog(string path, ThemePalette theme)
    {
        _theme = theme;
        _model = TagEditorService.Read(path);

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = $"Edit Tags — {System.IO.Path.GetFileName(path)}";
        Size                = new Size(600, 510);
        MinimumSize         = new Size(560, 480);
        FormBorderStyle     = FormBorderStyle.Sizable;
        MaximizeBox         = false;
        StartPosition       = FormStartPosition.CenterParent;

        BuildLayout();
        PopulateFields();
        ApplyTheme();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 2,
            Padding     = new Padding(12, 10, 12, 8),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));

        root.Controls.Add(BuildCoverPanel(),   0, 0);
        root.Controls.Add(BuildFieldsPanel(),  1, 0);

        var bottomBar = BuildBottomBar();
        root.SetColumnSpan(bottomBar, 2);
        root.Controls.Add(bottomBar, 0, 1);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private Panel BuildCoverPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill };

        picCover.Location   = new Point(0, 0);
        picCover.Size       = new Size(132, 132);
        picCover.SizeMode   = PictureBoxSizeMode.Zoom;
        picCover.AllowDrop  = true;
        picCover.Cursor     = Cursors.Hand;
        picCover.DragOver  += (_, e) =>
        {
            if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
                e.Effect = DragDropEffects.Copy;
        };
        picCover.DragDrop    += PicCover_DragDrop;
        picCover.DoubleClick += (_, _) => ChangeCoverArt();

        btnChangeCover.Text      = "Change...";
        btnChangeCover.Location  = new Point(0, 136);
        btnChangeCover.Size      = new Size(132, 26);
        btnChangeCover.FlatStyle = FlatStyle.Flat;
        btnChangeCover.Font      = new Font("Segoe UI", 8.5f);
        btnChangeCover.Click    += (_, _) => ChangeCoverArt();

        btnRemoveCover.Text      = "Remove";
        btnRemoveCover.Location  = new Point(0, 165);
        btnRemoveCover.Size      = new Size(132, 26);
        btnRemoveCover.FlatStyle = FlatStyle.Flat;
        btnRemoveCover.Font      = new Font("Segoe UI", 8.5f);
        btnRemoveCover.Click    += (_, _) => RemoveCoverArt();

        panel.Controls.AddRange([picCover, btnChangeCover, btnRemoveCover]);
        return panel;
    }

    private Panel BuildFieldsPanel()
    {
        var grid = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            Padding     = new Padding(8, 0, 0, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        void ConfigNum(NumericUpDown n, decimal max)
        {
            n.Minimum       = 0;
            n.Maximum       = max;
            n.DecimalPlaces = 0;
            n.Font          = new Font("Segoe UI", 9.5f);
            n.Width         = 72;
        }

        ConfigNum(numTrack, 9999);
        ConfigNum(numDisc, 99);
        ConfigNum(numYear, 9999);
        ConfigNum(numBpm, 999);

        void AddFieldRow(string labelText, Control ctrl)
        {
            var lbl = MakeLabel(labelText);
            ctrl.Dock = DockStyle.Fill;
            ApplyFieldFont(ctrl);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.Controls.Add(lbl);
            grid.Controls.Add(ctrl);
        }

        void AddPairRow(string labelText, NumericUpDown first, string midLabel, NumericUpDown second)
        {
            var lbl   = MakeLabel(labelText);
            var panel = new Panel { Dock = DockStyle.Fill };
            first.Dock   = DockStyle.None;
            first.Location = new Point(0, 3);
            var midLbl = new Label
            {
                Text      = midLabel,
                AutoSize  = true,
                Location  = new Point(78, 6),
                Font      = new Font("Segoe UI", 9f),
            };
            second.Dock     = DockStyle.None;
            second.Location = new Point(78 + midLbl.PreferredWidth + 6, 3);
            panel.Controls.AddRange([first, midLbl, second]);
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            grid.Controls.Add(lbl);
            grid.Controls.Add(panel);
        }

        AddFieldRow("Title",        txtTitle);
        AddFieldRow("Artist",       txtArtist);
        AddFieldRow("Alb. Artist",  txtAlbumArtist);
        AddFieldRow("Album",        txtAlbum);
        AddPairRow("Track",  numTrack, "Disc", numDisc);
        AddPairRow("Year",   numYear,  "BPM",  numBpm);
        AddFieldRow("Genre",    txtGenre);
        AddFieldRow("Composer", txtComposer);

        // Comment: multiline, takes remaining space
        var commentLbl = MakeLabel("Comment");
        commentLbl.TextAlign = ContentAlignment.TopRight;
        commentLbl.Padding   = new Padding(0, 6, 6, 0);
        txtComment.Multiline   = true;
        txtComment.ScrollBars  = ScrollBars.Vertical;
        txtComment.Dock        = DockStyle.Fill;
        txtComment.Font        = new Font("Segoe UI", 9.5f);
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(commentLbl);
        grid.Controls.Add(txtComment);

        grid.RowCount = grid.RowStyles.Count;
        return grid;
    }

    private static Label MakeLabel(string text) => new()
    {
        Text      = text,
        AutoSize  = false,
        Dock      = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleRight,
        Padding   = new Padding(0, 0, 6, 0),
        Font      = new Font("Segoe UI", 9f),
    };

    private static void ApplyFieldFont(Control ctrl)
    {
        if (ctrl is TextBox tb) tb.Font = new Font("Segoe UI", 9.5f);
    }

    private Panel BuildBottomBar()
    {
        var bar = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 5,
            RowCount    = 1,
        };
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Fetch MB
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));   // spacer / status
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Revert
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Save
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));       // Cancel

        void StyleBtn(Button b, bool primary = false)
        {
            b.Size      = new Size(76, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            b.Anchor    = AnchorStyles.None;
            if (primary)
            {
                b.BackColor = _theme.AccentPrimaryColor;
                b.ForeColor = _theme.AccentContrastColor;
                b.FlatAppearance.BorderSize = 0;
            }
            else
            {
                b.BackColor = _theme.SurfaceRaisedColor;
                b.ForeColor = _theme.TextSecondaryColor;
                b.FlatAppearance.BorderColor = _theme.BorderStrongColor;
            }
        }

        btnFetchMb.Text    = "Fetch from MusicBrainz...";
        btnFetchMb.Size    = new Size(196, 30);
        btnFetchMb.FlatStyle = FlatStyle.Flat;
        btnFetchMb.Font    = new Font("Segoe UI", 9f);
        btnFetchMb.Anchor  = AnchorStyles.Left | AnchorStyles.Top;
        btnFetchMb.BackColor = _theme.SurfaceRaisedColor;
        btnFetchMb.ForeColor = _theme.TextSecondaryColor;
        btnFetchMb.FlatAppearance.BorderColor = _theme.BorderStrongColor;
        btnFetchMb.Click  += BtnFetchMb_Click;

        lblStatus.AutoSize  = false;
        lblStatus.Dock      = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Font      = new Font("Segoe UI", 8.5f);
        lblStatus.Padding   = new Padding(8, 0, 0, 0);

        btnRevert.Text   = "Revert";
        btnSave.Text     = "Save";
        btnCancel.Text   = "Cancel";
        StyleBtn(btnRevert);
        StyleBtn(btnSave, primary: true);
        StyleBtn(btnCancel);

        btnRevert.Click += (_, _) => { _model = TagEditorService.Read(_model.FilePath); PopulateFields(); };
        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        bar.Controls.Add(btnFetchMb, 0, 0);
        bar.Controls.Add(lblStatus,  1, 0);
        bar.Controls.Add(btnRevert,  2, 0);
        bar.Controls.Add(btnSave,    3, 0);
        bar.Controls.Add(btnCancel,  4, 0);
        return bar;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Data binding
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateFields()
    {
        txtTitle.Text       = _model.Title       ?? "";
        txtArtist.Text      = _model.Artist      ?? "";
        txtAlbumArtist.Text = _model.AlbumArtist ?? "";
        txtAlbum.Text       = _model.Album       ?? "";
        numTrack.Value      = Math.Min(_model.TrackNumber, 9999);
        numDisc.Value       = Math.Min(_model.DiscNumber, 99);
        numYear.Value       = Math.Min(_model.Year, 9999);
        numBpm.Value        = Math.Min(_model.BPM, 999);
        txtGenre.Text       = _model.Genre       ?? "";
        txtComposer.Text    = _model.Composer    ?? "";
        txtComment.Text     = _model.Comment     ?? "";
        SetCoverImage(_model.CoverArt);
        lblStatus.Text = "";
    }

    private void CollectToModel()
    {
        _model.Title       = Trim(txtTitle.Text);
        _model.Artist      = Trim(txtArtist.Text);
        _model.AlbumArtist = Trim(txtAlbumArtist.Text);
        _model.Album       = Trim(txtAlbum.Text);
        _model.TrackNumber = (uint)numTrack.Value;
        _model.DiscNumber  = (uint)numDisc.Value;
        _model.Year        = (uint)numYear.Value;
        _model.BPM         = (uint)numBpm.Value;
        _model.Genre       = Trim(txtGenre.Text);
        _model.Composer    = Trim(txtComposer.Text);
        _model.Comment     = Trim(txtComment.Text);
    }

    private static string? Trim(string s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    // ─────────────────────────────────────────────────────────────────────────
    // Theming
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;

        foreach (var ctrl in AllDescendants(this))
        {
            switch (ctrl)
            {
                case Label lbl:
                    lbl.ForeColor = _theme.TextSecondaryColor;
                    lbl.BackColor = Color.Transparent;
                    break;
                case TextBox tb:
                    tb.BackColor   = _theme.SurfaceRaisedColor;
                    tb.ForeColor   = _theme.TextPrimaryColor;
                    tb.BorderStyle = BorderStyle.FixedSingle;
                    break;
                case NumericUpDown nud:
                    nud.BackColor = _theme.SurfaceRaisedColor;
                    nud.ForeColor = _theme.TextPrimaryColor;
                    break;
            }
        }

        picCover.BackColor   = _theme.SurfaceBackColor;
        picCover.BorderStyle = BorderStyle.FixedSingle;
        lblStatus.ForeColor  = _theme.TextMutedColor;
        lblStatus.BackColor  = Color.Transparent;
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
    // Cover art
    // ─────────────────────────────────────────────────────────────────────────

    private void SetCoverImage(byte[]? art)
    {
        var old = picCover.Image;
        if (art is not null)
        {
            try
            {
                using var ms  = new MemoryStream(art);
                using var tmp = Image.FromStream(ms);
                picCover.Image = new Bitmap(tmp);
            }
            catch
            {
                picCover.Image = null;
            }
        }
        else
        {
            picCover.Image = null;
        }
        old?.Dispose();
    }

    private void ChangeCoverArt()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif|All Files|*.*",
            Title  = "Select Cover Art",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        LoadCoverArtFromFile(dlg.FileName);
    }

    private void RemoveCoverArt()
    {
        _model.CoverArt = null;
        SetCoverImage(null);
    }

    private void PicCover_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            LoadCoverArtFromFile(files[0]);
    }

    private void LoadCoverArtFromFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            _model.CoverArt = bytes;
            SetCoverImage(bytes);
        }
        catch { }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Save / MusicBrainz
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        CollectToModel();
        try
        {
            TagEditorService.Write(_model);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"Save failed: {ex.Message}";
        }
    }

    private async void BtnFetchMb_Click(object? sender, EventArgs e)
    {
        var title  = txtTitle.Text.Trim();
        var artist = txtArtist.Text.Trim();
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist))
        {
            lblStatus.Text = "Enter a title or artist first.";
            return;
        }

        btnFetchMb.Enabled = false;
        lblStatus.Text     = "Searching MusicBrainz...";

        try
        {
            var results = await MusicBrainzClient.SearchAsync(title, artist);
            if (results.Count == 0)
            {
                lblStatus.Text = "No results found.";
                return;
            }

            using var picker = new MusicBrainzPickerDialog(results, _theme);
            if (picker.ShowDialog(this) != DialogResult.OK || picker.Selected is not { } sel)
            {
                lblStatus.Text = "";
                return;
            }

            if (!string.IsNullOrEmpty(sel.Title))   txtTitle.Text       = sel.Title;
            if (!string.IsNullOrEmpty(sel.Artist))  txtArtist.Text      = sel.Artist;
            if (!string.IsNullOrEmpty(sel.Album))   txtAlbum.Text       = sel.Album;
            if (sel.Year        > 0)                numYear.Value       = Math.Min(sel.Year, 9999);
            if (sel.TrackNumber > 0)                numTrack.Value      = Math.Min(sel.TrackNumber, 9999);

            lblStatus.Text = "Fetching cover art...";
            var art = await MusicBrainzClient.FetchCoverArtAsync(sel.ReleaseId);
            if (art is not null)
            {
                _model.CoverArt = art;
                SetCoverImage(art);
                lblStatus.Text = "Applied from MusicBrainz.";
            }
            else
            {
                lblStatus.Text = "Applied (no cover art found).";
            }
        }
        catch (Exception ex)
        {
            lblStatus.Text = $"MusicBrainz error: {ex.Message}";
        }
        finally
        {
            btnFetchMb.Enabled = true;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            picCover.Image?.Dispose();
            picCover.Image = null;
        }
        base.Dispose(disposing);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// MusicBrainz recording picker
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class MusicBrainzPickerDialog : Form
{
    public MusicBrainzRecording? Selected { get; private set; }

    private readonly ListView list  = new();
    private readonly Button   btnOk = new();

    public MusicBrainzPickerDialog(List<MusicBrainzRecording> results, ThemePalette theme)
    {
        Text            = "Select Recording — MusicBrainz";
        Size            = new Size(580, 360);
        MinimumSize     = new Size(480, 300);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = theme.WindowBackColor;
        ForeColor       = theme.TextPrimaryColor;

        list.View           = View.Details;
        list.FullRowSelect  = true;
        list.MultiSelect    = false;
        list.GridLines      = false;
        list.HeaderStyle    = ColumnHeaderStyle.Nonclickable;
        list.Dock           = DockStyle.Fill;
        list.BackColor      = theme.WindowBackColor;
        list.ForeColor      = theme.TextPrimaryColor;
        list.Font           = new Font("Segoe UI", 9.5f);
        list.Columns.Add("Title",  220);
        list.Columns.Add("Artist", 150);
        list.Columns.Add("Album",  140);
        list.Columns.Add("Year",    48);
        list.DoubleClick += (_, _) => Confirm();

        foreach (var r in results)
        {
            var item = new ListViewItem(r.Title);
            item.SubItems.Add(r.Artist);
            item.SubItems.Add(r.Album);
            item.SubItems.Add(r.Year > 0 ? r.Year.ToString() : "");
            item.Tag = r;
            list.Items.Add(item);
        }

        if (list.Items.Count > 0)
            list.Items[0].Selected = true;

        var bottomPanel = new Panel { Dock = DockStyle.Bottom, Height = 46 };
        var btnCancel = new Button
        {
            Text      = "Cancel",
            Size      = new Size(80, 30),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Anchor    = AnchorStyles.Right | AnchorStyles.Top,
            BackColor = theme.SurfaceRaisedColor,
            ForeColor = theme.TextSecondaryColor,
        };
        btnCancel.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnCancel.Location = new Point(0, 8);
        btnCancel.Click   += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        btnOk.Text      = "Apply";
        btnOk.Size      = new Size(80, 30);
        btnOk.FlatStyle = FlatStyle.Flat;
        btnOk.Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
        btnOk.Anchor    = AnchorStyles.Right | AnchorStyles.Top;
        btnOk.BackColor = theme.AccentPrimaryColor;
        btnOk.ForeColor = theme.AccentContrastColor;
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Location  = new Point(0, 8);
        btnOk.Click    += (_, _) => Confirm();

        bottomPanel.Resize += (_, _) =>
        {
            btnOk.Left     = bottomPanel.Width - 80 - 8;
            btnCancel.Left = bottomPanel.Width - 80 - 8 - 88;
        };
        bottomPanel.BackColor = theme.SurfaceBackColor;
        bottomPanel.Controls.AddRange([btnCancel, btnOk]);

        Controls.Add(list);
        Controls.Add(bottomPanel);
    }

    private void Confirm()
    {
        if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is MusicBrainzRecording rec)
        {
            Selected     = rec;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
