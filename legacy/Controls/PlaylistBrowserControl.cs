using System.Drawing;

namespace Spectralis;

internal sealed class PlaylistBrowserControl : UserControl
{
    private sealed record PlaylistTag(bool IsStatic, Guid Id);
    private List<Playlist>      _playlists      = [];
    private List<SmartPlaylist> _smartPlaylists = [];
    private MusicLibrary?       _library;

    public event EventHandler<string[]>? PlayRequested;
    public event EventHandler<Guid>?     EditRequested;
    public event EventHandler<Guid>?     EditSmartRequested;
    public event EventHandler<Guid>?     DeleteRequested;
    public event EventHandler<Guid>?     DeleteSmartRequested;
    public event EventHandler?           NewPlaylistRequested;
    public event EventHandler?           NewSmartPlaylistRequested;
    public event EventHandler?           ImportRequested;

    // ── Layout ───────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel rootLayout   = new();
    private readonly Panel            headerPanel  = new();
    private readonly Panel            contentPanel = new();
    private readonly Label            lblStatus    = new();

    // ── Header ───────────────────────────────────────────────────────────────
    private readonly Button btnNew      = new();
    private readonly Button btnSmart    = new();
    private readonly Button btnImport   = new();

    // ── List ─────────────────────────────────────────────────────────────────
    private readonly ListView list      = new();

    public PlaylistBrowserControl()
    {
        DoubleBuffered = true;
        BuildLayout();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        rootLayout.Dock        = DockStyle.Fill;
        rootLayout.RowCount    = 3;
        rootLayout.ColumnCount = 1;
        rootLayout.Margin      = Padding.Empty;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        BuildHeader();
        BuildList();

        lblStatus.Dock      = DockStyle.Fill;
        lblStatus.Font      = new Font("Segoe UI", 8.5f);
        lblStatus.Padding   = new Padding(8, 0, 0, 0);
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;

        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(list);

        rootLayout.Controls.Add(headerPanel,  0, 0);
        rootLayout.Controls.Add(contentPanel, 0, 1);
        rootLayout.Controls.Add(lblStatus,    0, 2);
        Controls.Add(rootLayout);
        ResumeLayout(false);
    }

    private void BuildHeader()
    {
        headerPanel.Dock    = DockStyle.Fill;
        headerPanel.Padding = new Padding(8, 8, 8, 8);

        btnNew.Text      = "New Playlist";
        btnNew.Font      = new Font("Segoe UI", 9f);
        btnNew.FlatStyle = FlatStyle.Flat;
        btnNew.Dock      = DockStyle.Left;
        btnNew.Width     = 96;
        btnNew.Click    += (_, _) => NewPlaylistRequested?.Invoke(this, EventArgs.Empty);

        var spacer1 = new Panel { Dock = DockStyle.Left, Width = 6 };

        btnSmart.Text      = "New Smart";
        btnSmart.Font      = new Font("Segoe UI", 9f);
        btnSmart.FlatStyle = FlatStyle.Flat;
        btnSmart.Dock      = DockStyle.Left;
        btnSmart.Width     = 84;
        btnSmart.Click    += (_, _) => NewSmartPlaylistRequested?.Invoke(this, EventArgs.Empty);

        btnImport.Text      = "Import M3U...";
        btnImport.Font      = new Font("Segoe UI", 9f);
        btnImport.FlatStyle = FlatStyle.Flat;
        btnImport.Dock      = DockStyle.Right;
        btnImport.Width     = 96;
        btnImport.Click    += (_, _) => ImportRequested?.Invoke(this, EventArgs.Empty);

        headerPanel.Controls.Add(btnNew);
        headerPanel.Controls.Add(spacer1);
        headerPanel.Controls.Add(btnSmart);
        headerPanel.Controls.Add(btnImport);
    }

    private void BuildList()
    {
        list.Dock          = DockStyle.Fill;
        list.View          = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect   = false;
        list.GridLines     = false;
        list.HeaderStyle   = ColumnHeaderStyle.Nonclickable;
        list.BorderStyle   = BorderStyle.None;
        list.Font          = new Font("Segoe UI", 9.5f);
        list.Columns.Add("Name",  260);
        list.Columns.Add("Tracks", 56);
        list.Columns.Add("Type",   60);
        list.DoubleClick += List_DoubleClick;

        var ctx              = new ContextMenuStrip();
        var ctxPlay          = new ToolStripMenuItem { Text = "▶  Play" };
        var ctxEdit          = new ToolStripMenuItem { Text = "Edit..." };
        var ctxExport        = new ToolStripMenuItem { Text = "Export to M3U..." };
        var ctxDelete        = new ToolStripMenuItem { Text = "Delete" };
        ctx.Items.AddRange([ctxPlay, ctxEdit, ctxExport, new ToolStripSeparator(), ctxDelete]);
        ctx.Opening += (_, _) =>
        {
            var hasSelection = list.SelectedItems.Count > 0;
            foreach (ToolStripItem item in ctx.Items) item.Enabled = hasSelection;
        };
        ctxPlay.Click   += (_, _) => ActivateSelected();
        ctxEdit.Click   += (_, _) => EditSelected();
        ctxExport.Click += (_, _) => ExportSelected();
        ctxDelete.Click += (_, _) => DeleteSelected();
        list.ContextMenuStrip = ctx;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void SetLibrary(MusicLibrary library) => _library = library;

    public void LoadPlaylists(List<Playlist> playlists, List<SmartPlaylist> smart)
    {
        _playlists      = playlists;
        _smartPlaylists = smart;
        Refresh();
    }

    public void AddPlaylist(Playlist pl)
    {
        _playlists.Add(pl);
        Refresh();
    }

    public void AddSmartPlaylist(SmartPlaylist pl)
    {
        _smartPlaylists.Add(pl);
        Refresh();
    }

    public void UpdatePlaylist(Playlist pl)
    {
        var idx = _playlists.FindIndex(p => p.Id == pl.Id);
        if (idx >= 0) _playlists[idx] = pl;
        Refresh();
    }

    public void UpdateSmartPlaylist(SmartPlaylist pl)
    {
        var idx = _smartPlaylists.FindIndex(p => p.Id == pl.Id);
        if (idx >= 0) _smartPlaylists[idx] = pl;
        Refresh();
    }

    public void RemovePlaylist(Guid id)
    {
        _playlists.RemoveAll(p => p.Id == id);
        Refresh();
    }

    public void RemoveSmartPlaylist(Guid id)
    {
        _smartPlaylists.RemoveAll(p => p.Id == id);
        Refresh();
    }

    public new void Refresh()
    {
        list.BeginUpdate();
        list.Items.Clear();

        foreach (var pl in _playlists)
        {
            var item = new ListViewItem(pl.Name);
            item.SubItems.Add(pl.Items.Count.ToString());
            item.SubItems.Add("Static");
            item.Tag = new PlaylistTag(true, pl.Id);
            list.Items.Add(item);
        }

        foreach (var pl in _smartPlaylists)
        {
            var count = _library is not null ? SmartPlaylistEvaluator.Evaluate(pl, _library).Count : 0;
            var item  = new ListViewItem($"★ {pl.Name}");
            item.SubItems.Add(count.ToString());
            item.SubItems.Add("Smart");
            item.Tag = new PlaylistTag(false, pl.Id);
            list.Items.Add(item);
        }

        list.EndUpdate();
        UpdateStatus();
    }

    public void ApplyTheme(ThemePalette theme)
    {
        BackColor             = theme.WindowBackColor;
        headerPanel.BackColor = theme.SurfaceBackColor;
        contentPanel.BackColor = theme.WindowBackColor;

        btnNew.BackColor    = theme.SurfaceRaisedColor;
        btnNew.ForeColor    = theme.TextSecondaryColor;
        btnNew.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnSmart.BackColor  = theme.SurfaceRaisedColor;
        btnSmart.ForeColor  = theme.TextSecondaryColor;
        btnSmart.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnImport.BackColor = theme.SurfaceRaisedColor;
        btnImport.ForeColor = theme.TextSecondaryColor;
        btnImport.FlatAppearance.BorderColor = theme.BorderStrongColor;

        list.BackColor       = theme.WindowBackColor;
        list.ForeColor       = theme.TextPrimaryColor;

        lblStatus.BackColor  = theme.SurfaceBackColor;
        lblStatus.ForeColor  = theme.TextMutedColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    private void UpdateStatus()
    {
        var total = _playlists.Count + _smartPlaylists.Count;
        lblStatus.Text = total == 1 ? "1 playlist" : $"{total} playlists";
    }

    private void List_DoubleClick(object? sender, EventArgs e)
    {
        ActivateSelected();
    }

    private void ActivateSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        if (list.SelectedItems[0].Tag is not PlaylistTag tag) return;
        var (isStatic, id) = tag;
        string[] paths;
        if (isStatic)
        {
            var pl = _playlists.Find(p => p.Id == id);
            paths = pl?.Items.Select(i => i.Path).ToArray() ?? [];
        }
        else
        {
            var pl = _smartPlaylists.Find(p => p.Id == id);
            paths = pl is not null && _library is not null
                ? [.. SmartPlaylistEvaluator.Evaluate(pl, _library)]
                : [];
        }
        if (paths.Length > 0)
            PlayRequested?.Invoke(this, paths);
    }

    private void EditSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        if (list.SelectedItems[0].Tag is not PlaylistTag tag) return;
        var (isStatic, id) = tag;
        if (isStatic)
            EditRequested?.Invoke(this, id);
        else
            EditSmartRequested?.Invoke(this, id);
    }

    private void ExportSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        if (list.SelectedItems[0].Tag is not PlaylistTag tag) return;
        var (isStatic, id) = tag;

        IEnumerable<PlaylistItem>? items = null;
        string defaultName = "playlist";
        if (isStatic)
        {
            var pl = _playlists.Find(p => p.Id == id);
            items       = pl?.Items;
            defaultName = pl?.Name ?? defaultName;
        }
        else if (_library is not null)
        {
            var pl = _smartPlaylists.Find(p => p.Id == id);
            defaultName = pl?.Name ?? defaultName;
            if (pl is not null)
            {
                var paths = SmartPlaylistEvaluator.Evaluate(pl, _library);
                items = paths.Select(path =>
                {
                    var t = _library.Find(path);
                    return new PlaylistItem { Path = path, Title = t?.Title, Artist = t?.Artist };
                });
            }
        }

        if (items is null) return;

        using var dlg = new SaveFileDialog
        {
            Filter      = "M3U Playlist|*.m3u8|Classic M3U|*.m3u",
            FileName    = $"{defaultName}.m3u8",
            Title       = "Export Playlist",
        };
        if (dlg.ShowDialog(FindForm()) != DialogResult.OK) return;

        try { M3uParser.Export(dlg.FileName, items); }
        catch { }
    }

    private void DeleteSelected()
    {
        if (list.SelectedItems.Count == 0) return;
        if (list.SelectedItems[0].Tag is not PlaylistTag tag) return;
        var (isStatic, id) = tag;
        var name = list.SelectedItems[0].Text.TrimStart('★', ' ');
        var confirm = MessageBox.Show(FindForm(),
            $"Delete \"{name}\"?",
            "Delete Playlist",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes) return;

        if (isStatic) DeleteRequested?.Invoke(this, id);
        else          DeleteSmartRequested?.Invoke(this, id);
    }
}
