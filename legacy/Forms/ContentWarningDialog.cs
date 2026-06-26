using System.Drawing;
using System.IO;

namespace Spectralis;

/// <summary>
/// Modal popup shown before playing a track that has content-warning tags.
/// DialogResult.OK means "Play Anyway"; anything else means "skip / cancel".
/// </summary>
internal sealed class ContentWarningDialog : Form
{
    public ContentWarningDialog(string[] tags, string trackName, ThemePalette palette)
    {
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize    = new Size(440, 300);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox  = false;
        MinimizeBox  = false;
        ShowIcon     = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Content Warning";

        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        // ── Root layout ───────────────────────────────────────────────────
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(28, 24, 28, 20)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // heading
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // track name
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // intro label
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F)); // tag chips
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // buttons
        layout.BackColor = palette.WindowBackColor;
        Controls.Add(layout);

        // ── Heading ───────────────────────────────────────────────────────
        var lblHeading = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = Padding.Empty,
            Text = "⚠  Content Warning"
        };
        layout.Controls.Add(lblHeading, 0, 0);

        // ── Track name ────────────────────────────────────────────────────
        var lblTrack = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextMutedColor,
            Margin = new Padding(0, 6, 0, 12),
            MaximumSize = new Size(384, 0),
            Text = trackName
        };
        layout.Controls.Add(lblTrack, 0, 1);

        // ── Intro text ────────────────────────────────────────────────────
        var lblIntro = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25f, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 0, 0, 12),
            Text = "This track has the following content warnings:"
        };
        layout.Controls.Add(lblIntro, 0, 2);

        // ── Tag chips ─────────────────────────────────────────────────────
        var tagFlow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 4),
            Padding = Padding.Empty,
            WrapContents = true,
            BackColor = palette.WindowBackColor
        };
        layout.Controls.Add(tagFlow, 0, 3);

        foreach (var tag in tags)
        {
            var chip = new Label
            {
                AutoSize = true,
                BackColor = palette.AccentSoftColor,
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = palette.AccentPrimaryColor,
                Margin = new Padding(0, 0, 6, 6),
                Padding = new Padding(10, 4, 10, 4),
                Text = tag
            };
            tagFlow.Controls.Add(chip);
        }

        // ── Buttons ───────────────────────────────────────────────────────
        var btnPanel = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 8, 0, 0),
            WrapContents = false,
            BackColor = palette.WindowBackColor
        };
        layout.Controls.Add(btnPanel, 0, 4);

        var btnPlay = new ModernButton
        {
            Text = "Play Anyway",
            Size = new Size(120, 38),
            Margin = new Padding(8, 0, 0, 0)
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnPlay, palette, palette.AccentPrimaryColor);
        btnPlay.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        btnPanel.Controls.Add(btnPlay);

        var btnCancel = new ModernButton
        {
            Text = "Cancel",
            Size = new Size(90, 38),
            Margin = Padding.Empty
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btnCancel, palette, palette.BorderStrongColor);
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        btnPanel.Controls.Add(btnCancel);

    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
