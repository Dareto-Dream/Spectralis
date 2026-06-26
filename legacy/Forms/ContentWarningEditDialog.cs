using System.Drawing;
using System.IO;

namespace Spectralis;

/// <summary>
/// Lets the user set or clear content-warning tags for a single track.
/// Tags are entered as a comma-separated list.
/// DialogResult.OK means the edit was saved.
/// </summary>
internal sealed class ContentWarningEditDialog : Form
{
    private readonly string filePath;
    private readonly TextBox txtTags;

    public ContentWarningEditDialog(string filePath, ThemePalette palette)
    {
        this.filePath = filePath;

        AutoScaleMode   = AutoScaleMode.Font;
        ClientSize      = new Size(460, 246);
        DoubleBuffered  = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        ShowIcon        = false;
        ShowInTaskbar   = false;
        StartPosition   = FormStartPosition.CenterParent;
        Text            = "Content Warnings";

        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        // ── Layout ────────────────────────────────────────────────────────
        var layout = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(28, 22, 28, 20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // title
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // track name
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // description
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // text box
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // buttons
        layout.BackColor = palette.WindowBackColor;
        Controls.Add(layout);

        // ── Title ─────────────────────────────────────────────────────────
        var lblTitle = new Label
        {
            AutoSize  = true,
            Font      = new Font("Segoe UI Semibold", 13f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin    = Padding.Empty,
            Text      = "Content Warnings"
        };
        layout.Controls.Add(lblTitle, 0, 0);

        // ── Track name ────────────────────────────────────────────────────
        var trackName = Path.GetFileNameWithoutExtension(filePath);
        var lblTrack  = new Label
        {
            AutoSize    = true,
            Font        = new Font("Segoe UI", 8.75f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor   = palette.TextMutedColor,
            Margin      = new Padding(0, 5, 0, 12),
            MaximumSize = new Size(400, 0),
            Text        = trackName
        };
        layout.Controls.Add(lblTrack, 0, 1);

        // ── Description ───────────────────────────────────────────────────
        var lblDesc = new Label
        {
            AutoSize    = true,
            Font        = new Font("Segoe UI", 8.75f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor   = palette.TextSecondaryColor,
            Margin      = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(400, 0),
            Text        = "Enter content warnings separated by commas. A popup will appear before this track plays."
        };
        layout.Controls.Add(lblDesc, 0, 2);

        // ── Text box ──────────────────────────────────────────────────────
        var existingTags = TrackContentWarningStore.Get(filePath);
        txtTags = new TextBox
        {
            Dock         = DockStyle.Fill,
            Font         = new Font("Segoe UI", 9.25f, FontStyle.Regular, GraphicsUnit.Point),
            Margin       = new Padding(0, 0, 0, 14),
            PlaceholderText = "e.g. violence, flashing lights, loud sounds",
            Size         = new Size(404, 28),
            Text         = string.Join(", ", existingTags)
        };
        txtTags.BackColor = palette.SurfaceAltBackColor;
        txtTags.ForeColor = palette.TextPrimaryColor;
        txtTags.BorderStyle = BorderStyle.FixedSingle;
        layout.Controls.Add(txtTags, 0, 3);

        // ── Buttons ───────────────────────────────────────────────────────
        var btnPanel = new FlowLayoutPanel
        {
            AutoSize      = true,
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin        = Padding.Empty,
            WrapContents  = false,
            BackColor     = palette.WindowBackColor
        };
        layout.Controls.Add(btnPanel, 0, 4);

        var btnSave = new ModernButton
        {
            Text   = "Save",
            Size   = new Size(90, 38),
            Margin = new Padding(8, 0, 0, 0)
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnSave, palette, palette.AccentPrimaryColor);
        btnSave.Click += BtnSave_Click;
        btnPanel.Controls.Add(btnSave);

        var btnClear = new ModernButton
        {
            Text   = "Clear",
            Size   = new Size(76, 38),
            Margin = new Padding(8, 0, 0, 0)
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btnClear, palette, palette.DangerColor);
        btnClear.Click += (_, _) =>
        {
            TrackContentWarningStore.Clear(filePath);
            DialogResult = DialogResult.OK;
            Close();
        };
        btnPanel.Controls.Add(btnClear);

        var btnCancel = new ModernButton
        {
            Text   = "Cancel",
            Size   = new Size(86, 38),
            Margin = Padding.Empty
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btnCancel, palette, palette.BorderStrongColor);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnCancel);

        Shown += (_, _) =>
        {
            txtTags.Focus();
            txtTags.SelectAll();
        };
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var raw  = txtTags.Text ?? "";
        var tags = raw
            .Split(',')
            .Select(static t => t.Trim())
            .Where(static t => !string.IsNullOrEmpty(t))
            .ToArray();

        TrackContentWarningStore.Set(filePath, tags);
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
