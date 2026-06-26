using System.Drawing;
using System.IO;

namespace Spectralis;

// ─────────────────────────────────────────────────────────────────────────────
// Playlist editor — rename + drag-reorder track list
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class PlaylistEditorDialog : Form
{
    private readonly Playlist      _playlist;
    private readonly List<PlaylistItem> _items;
    private readonly MusicLibrary? _library;
    private readonly PlayQueue?    _queue;
    private int                    _dragFromIndex = -1;

    public Playlist Result => _playlist;

    private readonly TextBox  txtName   = new();
    private readonly ListBox  listItems = new();
    private readonly Button   btnAddFiles  = new();
    private readonly Button   btnAddQueue  = new();
    private readonly Button   btnRemove    = new();
    private readonly Button   btnSave      = new();
    private readonly Button   btnCancel    = new();
    private readonly Label    lblStatus    = new();

    public PlaylistEditorDialog(
        Playlist playlist,
        ThemePalette theme,
        MusicLibrary? library = null,
        PlayQueue? queue = null)
    {
        _playlist = playlist;
        _items    = new List<PlaylistItem>(playlist.Items);
        _library  = library;
        _queue    = queue;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Edit Playlist";
        Size                = new Size(500, 480);
        MinimumSize         = new Size(420, 380);
        FormBorderStyle     = FormBorderStyle.Sizable;
        MaximizeBox         = false;
        StartPosition       = FormStartPosition.CenterParent;

        BuildLayout();
        PopulateList();
        ApplyTheme(theme);
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
            ColumnCount = 1,
            RowCount    = 4,
            Padding     = new Padding(12, 10, 12, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // name row
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));   // add/remove buttons
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // list
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));   // bottom bar

        // Name row
        var nameRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 46));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var lblName = new Label
        {
            Text = "Name",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9f),
        };
        txtName.Text = _playlist.Name;
        txtName.Dock = DockStyle.Fill;
        txtName.Font = new Font("Segoe UI", 10f);
        nameRow.Controls.Add(lblName);
        nameRow.Controls.Add(txtName);
        root.Controls.Add(nameRow, 0, 0);

        // Track action buttons
        var actionsRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };

        void StyleAction(Button b)
        {
            b.Size      = new Size(120, 28);
            b.FlatStyle = FlatStyle.Flat;
            b.Font      = new Font("Segoe UI", 9f);
            b.Margin    = new Padding(0, 4, 6, 0);
        }

        btnAddFiles.Text  = "Add Files...";
        btnAddQueue.Text  = "Add from Queue";
        btnRemove.Text    = "Remove";
        StyleAction(btnAddFiles);
        StyleAction(btnAddQueue);
        StyleAction(btnRemove);
        btnAddQueue.Enabled = _queue is not null;

        btnAddFiles.Click  += BtnAddFiles_Click;
        btnAddQueue.Click  += BtnAddQueue_Click;
        btnRemove.Click    += BtnRemove_Click;
        actionsRow.Controls.AddRange([btnAddFiles, btnAddQueue, btnRemove]);
        root.Controls.Add(actionsRow, 0, 1);

        // Track list
        listItems.Dock          = DockStyle.Fill;
        listItems.SelectionMode = SelectionMode.MultiExtended;
        listItems.Font          = new Font("Segoe UI", 9.5f);
        listItems.AllowDrop     = true;
        listItems.MouseDown    += ListItems_MouseDown;
        listItems.DragOver     += ListItems_DragOver;
        listItems.DragDrop     += ListItems_DragDrop;
        root.Controls.Add(listItems, 0, 2);

        // Bottom bar
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        lblStatus.Dock      = DockStyle.Fill;
        lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblStatus.Font      = new Font("Segoe UI", 8.5f);

        btnSave.Text    = "Save";
        btnCancel.Text  = "Cancel";
        foreach (var b in new[] { btnSave, btnCancel })
        {
            b.Size      = new Size(80, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            b.Anchor    = AnchorStyles.None;
        }
        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        bottom.Controls.Add(lblStatus,  0, 0);
        bottom.Controls.Add(btnSave,    1, 0);
        bottom.Controls.Add(btnCancel,  2, 0);
        root.Controls.Add(bottom, 0, 3);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private void PopulateList()
    {
        listItems.BeginUpdate();
        listItems.Items.Clear();
        foreach (var item in _items)
        {
            var label = BuildItemLabel(item);
            listItems.Items.Add(label);
        }
        listItems.EndUpdate();
        UpdateStatus();
    }

    private string BuildItemLabel(PlaylistItem item)
    {
        // Try library for up-to-date metadata
        var t = _library?.Find(item.Path);
        var title  = t?.Title  ?? item.Title  ?? Path.GetFileNameWithoutExtension(item.Path);
        var artist = t?.Artist ?? item.Artist ?? "";
        return string.IsNullOrEmpty(artist) ? title : $"{artist} — {title}";
    }

    private void UpdateStatus() =>
        lblStatus.Text = _items.Count == 1 ? "1 track" : $"{_items.Count} tracks";

    private void ApplyTheme(ThemePalette theme)
    {
        BackColor = theme.WindowBackColor;
        ForeColor = theme.TextPrimaryColor;

        txtName.BackColor   = theme.SurfaceRaisedColor;
        txtName.ForeColor   = theme.TextPrimaryColor;
        txtName.BorderStyle = BorderStyle.FixedSingle;
        listItems.BackColor = theme.SurfaceRaisedColor;
        listItems.ForeColor = theme.TextPrimaryColor;
        lblStatus.ForeColor = theme.TextMutedColor;

        foreach (var b in new[] { btnAddFiles, btnAddQueue, btnRemove })
        {
            b.BackColor = theme.SurfaceRaisedColor;
            b.ForeColor = theme.TextSecondaryColor;
            b.FlatAppearance.BorderColor = theme.BorderStrongColor;
        }

        btnSave.BackColor   = theme.AccentPrimaryColor;
        btnSave.ForeColor   = theme.AccentContrastColor;
        btnSave.FlatAppearance.BorderSize = 0;
        btnCancel.BackColor = theme.SurfaceRaisedColor;
        btnCancel.ForeColor = theme.TextSecondaryColor;
        btnCancel.FlatAppearance.BorderColor = theme.BorderStrongColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Button handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void BtnAddFiles_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Filter     = SupportedAudioFormats.OpenFileDialogFilter,
            Multiselect = true,
            Title      = "Add tracks to playlist",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        foreach (var path in dlg.FileNames.Where(File.Exists))
        {
            var t = _library?.Find(path);
            _items.Add(new PlaylistItem
            {
                Path   = path,
                Title  = t?.Title,
                Artist = t?.Artist,
                DurationSeconds = t?.DurationSeconds ?? 0,
            });
        }
        PopulateList();
    }

    private void BtnAddQueue_Click(object? sender, EventArgs e)
    {
        if (_queue is null) return;
        foreach (var path in _queue.Items.Where(File.Exists))
        {
            if (_items.Any(i => string.Equals(i.Path, path, StringComparison.OrdinalIgnoreCase)))
                continue;
            var t = _library?.Find(path);
            _items.Add(new PlaylistItem
            {
                Path   = path,
                Title  = t?.Title,
                Artist = t?.Artist,
                DurationSeconds = t?.DurationSeconds ?? 0,
            });
        }
        PopulateList();
    }

    private void BtnRemove_Click(object? sender, EventArgs e)
    {
        var indices = listItems.SelectedIndices.Cast<int>().OrderByDescending(i => i).ToList();
        foreach (var i in indices)
            _items.RemoveAt(i);
        PopulateList();
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "Unnamed Playlist";
        _playlist.Name  = name;
        _playlist.Items = _items;
        DialogResult    = DialogResult.OK;
        Close();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Drag-reorder
    // ─────────────────────────────────────────────────────────────────────────

    private void ListItems_MouseDown(object? sender, MouseEventArgs e)
    {
        _dragFromIndex = listItems.IndexFromPoint(e.Location);
        if (_dragFromIndex >= 0 && e.Button == MouseButtons.Left)
            listItems.DoDragDrop(_dragFromIndex, DragDropEffects.Move);
    }

    private static void ListItems_DragOver(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(typeof(int)) == true
            ? DragDropEffects.Move
            : DragDropEffects.None;
    }

    private void ListItems_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(typeof(int)) is not int from) return;
        var pt = listItems.PointToClient(new Point(e.X, e.Y));
        var to = listItems.IndexFromPoint(pt);
        if (to < 0) to = _items.Count - 1;
        if (from == to || from < 0) return;

        var item = _items[from];
        _items.RemoveAt(from);
        _items.Insert(to, item);
        PopulateList();
        listItems.SelectedIndex = to;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Simple name-input dialog (used to prompt for a new playlist name)
// ─────────────────────────────────────────────────────────────────────────────

internal sealed class NameInputDialog : Form
{
    public string InputValue => txtInput.Text.Trim();
    private readonly TextBox txtInput = new();

    public NameInputDialog(string title, string prompt, string defaultValue, ThemePalette theme)
    {
        Text            = title;
        Size            = new Size(360, 140);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = theme.WindowBackColor;
        ForeColor       = theme.TextPrimaryColor;

        var lbl = new Label
        {
            Text      = prompt,
            Location  = new Point(12, 12),
            Size      = new Size(328, 20),
            Font      = new Font("Segoe UI", 9f),
            ForeColor = theme.TextSecondaryColor,
        };

        txtInput.Text      = defaultValue;
        txtInput.Location  = new Point(12, 36);
        txtInput.Size      = new Size(328, 24);
        txtInput.Font      = new Font("Segoe UI", 10f);
        txtInput.BackColor = theme.SurfaceRaisedColor;
        txtInput.ForeColor = theme.TextPrimaryColor;
        txtInput.BorderStyle = BorderStyle.FixedSingle;
        txtInput.KeyDown  += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { DialogResult = DialogResult.OK; Close(); }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };

        var btnOk = new Button
        {
            Text      = "OK",
            Location  = new Point(328 - 160, 72),
            Size      = new Size(72, 28),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            BackColor = theme.AccentPrimaryColor,
            ForeColor = theme.AccentContrastColor,
        };
        btnOk.FlatAppearance.BorderSize = 0;
        btnOk.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };

        var btnCancel = new Button
        {
            Text      = "Cancel",
            Location  = new Point(328 - 80, 72),
            Size      = new Size(72, 28),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            BackColor = theme.SurfaceRaisedColor,
            ForeColor = theme.TextSecondaryColor,
        };
        btnCancel.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        Controls.AddRange([lbl, txtInput, btnOk, btnCancel]);
        ActiveControl = txtInput;
        txtInput.SelectAll();
    }
}
