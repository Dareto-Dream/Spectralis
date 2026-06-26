using System.Drawing;

namespace Spectralis;

internal sealed class LibraryBrowserControl : UserControl
{
    private MusicLibrary? _library;
    private List<LibraryTrack> _displayed = [];
    private CancellationTokenSource? _scanCts;
    private Func<string[]>? _getFolders;

    public event EventHandler<string>? TrackActivated;
    public event EventHandler? AddFolderRequested;
    public event EventHandler<string>? EditTagsRequested;
    public event EventHandler<string[]>? AnalyzeBpmRequested;

    // ── Layout ───────────────────────────────────────────────────────────────
    private readonly TableLayoutPanel rootLayout    = new();
    private readonly Panel             headerPanel  = new();
    private readonly Panel             contentPanel = new();
    private readonly Label             lblStatus    = new();

    // ── Header controls ──────────────────────────────────────────────────────
    private readonly TextBox   txtSearch  = new();
    private readonly ComboBox  cmbFilter  = new();
    private readonly Button    btnRescan  = new();
    private readonly Button    btnFolders = new();

    // ── Empty state ──────────────────────────────────────────────────────────
    private readonly Panel emptyState       = new();
    private readonly Label lblEmptyTitle    = new();
    private readonly Label lblEmptyHint     = new();
    private readonly Button btnAddFolder    = new();

    // ── Track grid ───────────────────────────────────────────────────────────
    private readonly DataGridView grid = new();
    private readonly DataGridViewTextBoxColumn colTitle    = new();
    private readonly DataGridViewTextBoxColumn colArtist   = new();
    private readonly DataGridViewTextBoxColumn colAlbum    = new();
    private readonly DataGridViewTextBoxColumn colDuration = new();
    private readonly DataGridViewTextBoxColumn colYear     = new();
    private readonly DataGridViewTextBoxColumn colPlays    = new();
    private readonly DataGridViewTextBoxColumn colBpm      = new();
    private readonly DataGridViewTextBoxColumn colKey      = new();

    public LibraryBrowserControl()
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

        // Root: header | content | status
        rootLayout.Dock       = DockStyle.Fill;
        rootLayout.RowCount   = 3;
        rootLayout.ColumnCount = 1;
        rootLayout.Margin     = Padding.Empty;
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        BuildHeader();
        BuildEmptyState();
        BuildGrid();

        // Status
        lblStatus.Dock      = DockStyle.Fill;
        lblStatus.Font      = new Font("Segoe UI", 8.5f);
        lblStatus.Padding   = new Padding(8, 0, 0, 0);
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Text      = "No tracks";

        // Content panel overlays grid + empty state
        contentPanel.Dock = DockStyle.Fill;
        contentPanel.Controls.Add(grid);
        contentPanel.Controls.Add(emptyState);
        emptyState.BringToFront();

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

        txtSearch.PlaceholderText = "Search...";
        txtSearch.Font            = new Font("Segoe UI", 9.5f);
        txtSearch.Dock            = DockStyle.Left;
        txtSearch.Width           = 220;
        txtSearch.BorderStyle     = BorderStyle.FixedSingle;
        txtSearch.TextChanged     += (_, _) => RefreshDisplay();

        var filterSpacer = new Panel { Dock = DockStyle.Left, Width = 6 };

        cmbFilter.DropDownStyle    = ComboBoxStyle.DropDownList;
        cmbFilter.Font             = new Font("Segoe UI", 9f);
        cmbFilter.Dock             = DockStyle.Left;
        cmbFilter.Width            = 118;
        cmbFilter.Items.AddRange(["All Tracks", "Artists", "Albums", "Genres"]);
        cmbFilter.SelectedIndex    = 0;
        cmbFilter.SelectedIndexChanged += (_, _) => RefreshDisplay();

        btnFolders.Text      = "Folders...";
        btnFolders.Font      = new Font("Segoe UI", 9f);
        btnFolders.FlatStyle = FlatStyle.Flat;
        btnFolders.Dock      = DockStyle.Right;
        btnFolders.Width     = 80;
        btnFolders.Click     += (_, _) => AddFolderRequested?.Invoke(this, EventArgs.Empty);

        var rescanSpacer = new Panel { Dock = DockStyle.Right, Width = 6 };

        btnRescan.Text      = "Rescan";
        btnRescan.Font      = new Font("Segoe UI", 9f);
        btnRescan.FlatStyle = FlatStyle.Flat;
        btnRescan.Dock      = DockStyle.Right;
        btnRescan.Width     = 68;
        btnRescan.Click     += async (_, _) => await StartScanAsync();

        headerPanel.Controls.Add(txtSearch);
        headerPanel.Controls.Add(filterSpacer);
        headerPanel.Controls.Add(cmbFilter);
        headerPanel.Controls.Add(btnFolders);
        headerPanel.Controls.Add(rescanSpacer);
        headerPanel.Controls.Add(btnRescan);
    }

    private void BuildEmptyState()
    {
        emptyState.Dock = DockStyle.Fill;

        lblEmptyTitle.Text      = "Your library is empty";
        lblEmptyTitle.Font      = new Font("Segoe UI Semibold", 15f, FontStyle.Bold);
        lblEmptyTitle.AutoSize  = false;
        lblEmptyTitle.TextAlign = ContentAlignment.MiddleCenter;
        lblEmptyTitle.Dock      = DockStyle.Fill;

        lblEmptyHint.Text      = "Add a folder to start building your music library.";
        lblEmptyHint.Font      = new Font("Segoe UI", 10f);
        lblEmptyHint.AutoSize  = false;
        lblEmptyHint.TextAlign = ContentAlignment.MiddleCenter;
        lblEmptyHint.Dock      = DockStyle.Bottom;
        lblEmptyHint.Height    = 32;

        btnAddFolder.Text      = "Add Folder...";
        btnAddFolder.Font      = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        btnAddFolder.FlatStyle = FlatStyle.Flat;
        btnAddFolder.Size      = new Size(160, 38);
        btnAddFolder.Anchor    = AnchorStyles.Bottom;
        btnAddFolder.Click    += (_, _) => AddFolderRequested?.Invoke(this, EventArgs.Empty);

        // Center the button horizontally at the bottom
        var btnRow = new Panel { Dock = DockStyle.Bottom, Height = 56 };
        btnRow.Resize += (_, _) =>
            btnAddFolder.Location = new Point((btnRow.Width - btnAddFolder.Width) / 2, 10);
        btnRow.Controls.Add(btnAddFolder);

        emptyState.Controls.Add(lblEmptyTitle);
        emptyState.Controls.Add(lblEmptyHint);
        emptyState.Controls.Add(btnRow);
    }

    private void BuildGrid()
    {
        grid.Dock                           = DockStyle.Fill;
        grid.VirtualMode                    = true;
        grid.ReadOnly                       = true;
        grid.AllowUserToAddRows             = false;
        grid.AllowUserToDeleteRows          = false;
        grid.AllowUserToResizeRows          = false;
        grid.MultiSelect                    = false;
        grid.SelectionMode                  = DataGridViewSelectionMode.FullRowSelect;
        grid.RowHeadersVisible              = false;
        grid.BorderStyle                    = BorderStyle.None;
        grid.AutoSizeColumnsMode            = DataGridViewAutoSizeColumnsMode.None;
        grid.ColumnHeadersHeightSizeMode    = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight            = 28;
        grid.DefaultCellStyle.Padding       = new Padding(4, 0, 4, 0);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(4, 0, 4, 0);
        grid.CellValueNeeded                += Grid_CellValueNeeded;
        grid.CellDoubleClick                += Grid_CellDoubleClick;
        grid.KeyDown                        += Grid_KeyDown;
        grid.CellMouseClick                 += Grid_CellMouseClick;

        var ctxGrid      = new ContextMenuStrip();
        var ctxEditTags  = new ToolStripMenuItem { Text = "Edit Tags..." };
        var ctxAnalyzeBpm = new ToolStripMenuItem { Text = "Analyze BPM + Key" };
        ctxEditTags.Click += (_, _) =>
        {
            var row = grid.CurrentCell?.RowIndex ?? -1;
            if (row >= 0 && row < _displayed.Count)
                EditTagsRequested?.Invoke(this, _displayed[row].Path);
        };
        ctxAnalyzeBpm.Click += (_, _) =>
        {
            var paths = grid.SelectedRows.Cast<DataGridViewRow>()
                .Select(r => r.Index)
                .Where(i => i >= 0 && i < _displayed.Count)
                .Select(i => _displayed[i].Path)
                .ToArray();
            if (paths.Length == 0)
            {
                var row = grid.CurrentCell?.RowIndex ?? -1;
                if (row >= 0 && row < _displayed.Count)
                    paths = [_displayed[row].Path];
            }
            if (paths.Length > 0)
                AnalyzeBpmRequested?.Invoke(this, paths);
        };
        ctxGrid.Items.Add(ctxEditTags);
        ctxGrid.Items.Add(ctxAnalyzeBpm);
        ctxGrid.Opening += (_, _) =>
        {
            var row = grid.CurrentCell?.RowIndex ?? -1;
            var fileExists = row >= 0 && row < _displayed.Count &&
                             System.IO.File.Exists(_displayed[row].Path);
            ctxEditTags.Enabled   = fileExists;
            ctxAnalyzeBpm.Enabled = row >= 0 && row < _displayed.Count;
        };
        grid.ContextMenuStrip = ctxGrid;

        colTitle.HeaderText               = "Title";
        colTitle.MinimumWidth             = 100;
        colTitle.AutoSizeMode             = DataGridViewAutoSizeColumnMode.Fill;
        colTitle.FillWeight               = 35;

        colArtist.HeaderText              = "Artist";
        colArtist.MinimumWidth            = 80;
        colArtist.AutoSizeMode            = DataGridViewAutoSizeColumnMode.Fill;
        colArtist.FillWeight              = 25;

        colAlbum.HeaderText               = "Album";
        colAlbum.MinimumWidth             = 80;
        colAlbum.AutoSizeMode             = DataGridViewAutoSizeColumnMode.Fill;
        colAlbum.FillWeight               = 25;

        colDuration.HeaderText            = "Time";
        colDuration.Width                 = 60;
        colDuration.AutoSizeMode          = DataGridViewAutoSizeColumnMode.None;
        colDuration.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        colYear.HeaderText                = "Year";
        colYear.Width                     = 52;
        colYear.AutoSizeMode              = DataGridViewAutoSizeColumnMode.None;

        colPlays.HeaderText               = "Plays";
        colPlays.Width                    = 52;
        colPlays.AutoSizeMode             = DataGridViewAutoSizeColumnMode.None;
        colPlays.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        colBpm.HeaderText               = "BPM";
        colBpm.Width                    = 56;
        colBpm.AutoSizeMode             = DataGridViewAutoSizeColumnMode.None;
        colBpm.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;

        colKey.HeaderText               = "Key";
        colKey.Width                    = 72;
        colKey.AutoSizeMode             = DataGridViewAutoSizeColumnMode.None;

        grid.Columns.AddRange([colTitle, colArtist, colAlbum, colDuration, colYear, colPlays, colBpm, colKey]);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    public void SetLibrary(MusicLibrary library)
    {
        if (_library is not null)
            _library.TracksChanged -= OnTracksChanged;
        _library = library;
        _library.TracksChanged += OnTracksChanged;
        RefreshDisplay();
    }

    public void SetGetFoldersCallback(Func<string[]> callback) =>
        _getFolders = callback;

    public void ApplyTheme(ThemePalette theme)
    {
        BackColor                = theme.WindowBackColor;
        headerPanel.BackColor    = theme.SurfaceBackColor;
        contentPanel.BackColor   = theme.WindowBackColor;

        txtSearch.BackColor   = theme.SurfaceRaisedColor;
        txtSearch.ForeColor   = theme.TextPrimaryColor;
        cmbFilter.BackColor   = theme.SurfaceRaisedColor;
        cmbFilter.ForeColor   = theme.TextPrimaryColor;

        btnRescan.BackColor   = theme.SurfaceRaisedColor;
        btnRescan.ForeColor   = theme.TextSecondaryColor;
        btnRescan.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnFolders.BackColor  = theme.SurfaceRaisedColor;
        btnFolders.ForeColor  = theme.TextSecondaryColor;
        btnFolders.FlatAppearance.BorderColor = theme.BorderStrongColor;

        emptyState.BackColor    = theme.WindowBackColor;
        lblEmptyTitle.BackColor = theme.WindowBackColor;
        lblEmptyTitle.ForeColor = theme.TextPrimaryColor;
        lblEmptyHint.BackColor  = theme.WindowBackColor;
        lblEmptyHint.ForeColor  = theme.TextSecondaryColor;
        btnAddFolder.BackColor  = theme.AccentPrimaryColor;
        btnAddFolder.ForeColor  = theme.AccentContrastColor;
        btnAddFolder.FlatAppearance.BorderSize  = 0;

        grid.BackgroundColor = theme.WindowBackColor;
        grid.GridColor       = theme.BorderColor;
        grid.DefaultCellStyle.BackColor          = theme.WindowBackColor;
        grid.DefaultCellStyle.ForeColor          = theme.TextPrimaryColor;
        grid.DefaultCellStyle.SelectionBackColor = theme.AccentSoftColor;
        grid.DefaultCellStyle.SelectionForeColor = theme.TextPrimaryColor;
        grid.DefaultCellStyle.Font               = new Font("Segoe UI", 9.5f);
        grid.AlternatingRowsDefaultCellStyle.BackColor          = theme.SurfaceBackColor;
        grid.AlternatingRowsDefaultCellStyle.ForeColor          = theme.TextPrimaryColor;
        grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = theme.AccentSoftColor;
        grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = theme.TextPrimaryColor;
        grid.ColumnHeadersDefaultCellStyle.BackColor = theme.SurfaceBackColor;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = theme.TextSecondaryColor;
        grid.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 8.5f);
        grid.EnableHeadersVisualStyles = false;

        lblStatus.BackColor = theme.SurfaceBackColor;
        lblStatus.ForeColor = theme.TextMutedColor;
    }

    public async Task StartScanAsync()
    {
        if (_library is null) return;
        var folders = _getFolders?.Invoke() ?? [];
        if (folders.Length == 0) return;

        _scanCts?.Cancel();
        _scanCts = new CancellationTokenSource();
        var cts = _scanCts;

        btnRescan.Enabled = false;
        lblStatus.Text    = "Scanning...";

        try
        {
            var worker   = new LibraryScanWorker();
            var progress = new Progress<int>(pct =>
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(() => lblStatus.Text = $"Scanning... {pct}%");
            });
            await worker.ScanAsync(folders, _library, progress, cts.Token);
            RefreshDisplay();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!IsDisposed)
                btnRescan.Enabled = true;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Internal
    // ─────────────────────────────────────────────────────────────────────────

    private void OnTracksChanged(object? sender, EventArgs e)
    {
        if (InvokeRequired)
            BeginInvoke(RefreshDisplay);
        else
            RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (_library is null)
        {
            _displayed = [];
            grid.RowCount     = 0;
            emptyState.Visible = true;
            grid.Visible       = false;
            lblStatus.Text     = "No library";
            return;
        }

        var filter = cmbFilter.SelectedItem?.ToString() ?? "All Tracks";
        _displayed = _library.Search(txtSearch.Text, filter);

        var hasAnyTracks = _library.Tracks.Count > 0;
        emptyState.Visible = !hasAnyTracks;
        grid.Visible       = hasAnyTracks;
        grid.RowCount      = _displayed.Count;
        grid.Refresh();

        var total = _library.Tracks.Count;
        lblStatus.Text = _displayed.Count == total
            ? $"{total:N0} tracks"
            : $"{_displayed.Count:N0} of {total:N0} tracks";
    }

    private void Grid_CellValueNeeded(object? sender, DataGridViewCellValueEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _displayed.Count) return;
        var t = _displayed[e.RowIndex];
        e.Value = e.ColumnIndex switch
        {
            0 => t.Title,
            1 => t.Artist,
            2 => t.Album,
            3 => t.DisplayDuration,
            4 => t.Year > 0 ? t.Year.ToString() : "",
            5 => t.PlayCount > 0 ? t.PlayCount.ToString() : "",
            6 => t.Bpm.HasValue ? t.Bpm.Value.ToString("F0") : "?",
            7 => t.Key ?? "",
            _ => ""
        };
    }

    private void Grid_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _displayed.Count) return;
        TrackActivated?.Invoke(this, _displayed[e.RowIndex].Path);
    }

    private void Grid_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            grid.CurrentCell = grid[e.ColumnIndex, e.RowIndex];
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        var row = grid.CurrentCell?.RowIndex ?? -1;
        if (row >= 0 && row < _displayed.Count)
            TrackActivated?.Invoke(this, _displayed[row].Path);
        e.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            if (_library is not null)
                _library.TracksChanged -= OnTracksChanged;
        }
        base.Dispose(disposing);
    }
}
