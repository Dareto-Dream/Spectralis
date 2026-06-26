using System.Drawing;

namespace Spectralis;

internal sealed class LibrarySettingsDialog : Form
{
    private readonly AppSettings _settings;
    private readonly Action      _onRescan;
    private readonly ListBox     lstFolders;
    private readonly ModernSwitch chkAutoScan;

    public LibrarySettingsDialog(AppSettings settings, Action onRescan, ThemePalette theme)
    {
        _settings = settings;
        _onRescan = onRescan;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Library Settings";
        ClientSize          = new Size(500, 400);
        FormBorderStyle     = FormBorderStyle.FixedDialog;
        MaximizeBox         = false;
        MinimizeBox         = false;
        StartPosition       = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 4,
            Padding     = new Padding(16),
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Row 0: header
        var lblTitle = new Label
        {
            Text     = "Watched Folders",
            Font     = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            AutoSize = true,
            Margin   = new Padding(0, 0, 0, 2),
        };
        var lblHint = new Label
        {
            Text     = "Spectralis scans these folders and indexes your music.",
            Font     = new Font("Segoe UI", 9f),
            AutoSize = true,
            Margin   = new Padding(0, 0, 0, 10),
        };
        var headerStack = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            AutoSize      = true,
            WrapContents  = false,
            Margin        = Padding.Empty,
        };
        headerStack.Controls.Add(lblTitle);
        headerStack.Controls.Add(lblHint);

        // Row 1: folder list
        lstFolders = new ListBox
        {
            Dock          = DockStyle.Fill,
            Font          = new Font("Segoe UI", 9.5f),
            IntegralHeight = false,
            Margin        = Padding.Empty,
        };
        foreach (var f in settings.LibraryFolders)
            lstFolders.Items.Add(f);

        // Row 2: folder action buttons
        var folderBtnRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize      = true,
            WrapContents  = false,
            Margin        = new Padding(0, 8, 0, 4),
        };
        var btnAdd = new Button
        {
            Text      = "Add Folder...",
            Size      = new Size(118, 30),
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(0, 0, 6, 0),
        };
        btnAdd.Click += BtnAdd_Click;

        var btnRemove = new Button
        {
            Text      = "Remove",
            Size      = new Size(80, 30),
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(0, 0, 6, 0),
        };
        btnRemove.Click += (_, _) =>
        {
            if (lstFolders.SelectedIndex >= 0)
                lstFolders.Items.RemoveAt(lstFolders.SelectedIndex);
        };

        var btnRescan = new Button
        {
            Text      = "Rescan Now",
            Size      = new Size(120, 30),
            FlatStyle = FlatStyle.Flat,
        };
        btnRescan.Click += (_, _) =>
        {
            Save();
            Close();
            _onRescan();
        };
        folderBtnRow.Controls.AddRange([btnAdd, btnRemove, btnRescan]);

        // Row 3: options + action buttons
        var bottomRow = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 1,
            AutoSize    = true,
            Margin      = new Padding(0, 6, 0, 0),
        };
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottomRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottomRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        chkAutoScan = new ModernSwitch
        {
            Checked = settings.LibraryAutoScanOnOpen,
            Margin  = new Padding(0, 6, 6, 0),
        };
        var lblAutoScan = new Label
        {
            Text      = "Auto-scan on startup",
            Font      = new Font("Segoe UI", 9.5f),
            AutoSize  = true,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var optLeft = new FlowLayoutPanel
        {
            Dock         = DockStyle.Fill,
            AutoSize     = false,
            WrapContents = false,
        };
        optLeft.Controls.Add(chkAutoScan);
        optLeft.Controls.Add(lblAutoScan);

        var actionBtns = new FlowLayoutPanel
        {
            AutoSize      = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };
        var btnOk = new Button
        {
            Text         = "OK",
            Size         = new Size(100, 32),
            FlatStyle    = FlatStyle.Flat,
            DialogResult = DialogResult.OK,
            Margin       = new Padding(0, 0, 6, 0),
        };
        btnOk.Click += (_, _) => Save();
        AcceptButton = btnOk;

        var btnCancel = new Button
        {
            Text         = "Cancel",
            Size         = new Size(80, 32),
            FlatStyle    = FlatStyle.Flat,
            DialogResult = DialogResult.Cancel,
        };
        CancelButton = btnCancel;
        actionBtns.Controls.AddRange([btnOk, btnCancel]);

        bottomRow.Controls.Add(optLeft, 0, 0);
        bottomRow.Controls.Add(actionBtns, 1, 0);

        root.Controls.Add(headerStack, 0, 0);
        root.Controls.Add(lstFolders, 0, 1);
        root.Controls.Add(folderBtnRow, 0, 2);
        root.Controls.Add(bottomRow, 0, 3);

        ApplyTheme(theme, [lblTitle, lblHint, lblAutoScan],
            [btnAdd, btnRemove, btnRescan, btnOk, btnCancel]);

        Controls.Add(root);
    }

    private void BtnAdd_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Select a music folder to add to your library"
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            lstFolders.Items.Add(dlg.SelectedPath);
    }

    private void Save()
    {
        _settings.LibraryFolders       = lstFolders.Items.Cast<string>().ToList();
        _settings.LibraryAutoScanOnOpen = chkAutoScan.Checked;
    }

    private void ApplyTheme(ThemePalette t, Label[] labels, Button[] buttons)
    {
        BackColor = t.WindowBackColor;
        ForeColor = t.TextPrimaryColor;

        foreach (var lbl in labels)
        {
            lbl.BackColor = t.WindowBackColor;
            lbl.ForeColor = t.TextPrimaryColor;
        }

        lstFolders.BackColor = t.SurfaceBackColor;
        lstFolders.ForeColor = t.TextPrimaryColor;

        for (var i = 0; i < buttons.Length; i++)
        {
            var btn = buttons[i];
            var isAccent = btn.DialogResult == DialogResult.OK || btn.Text == "Rescan Now";
            btn.BackColor = isAccent ? t.AccentPrimaryColor : t.SurfaceRaisedColor;
            btn.ForeColor = isAccent ? t.AccentContrastColor : t.TextPrimaryColor;
            btn.FlatAppearance.BorderColor = isAccent ? t.AccentPrimaryColor : t.BorderStrongColor;
        }
    }
}
