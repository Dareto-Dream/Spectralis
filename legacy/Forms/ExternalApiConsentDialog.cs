using System.Drawing;

namespace Spectralis;

public sealed class ExternalApiConsentDialog : Form
{
    private ExternalApiConsentDialog()
    {
        var palette = ThemePalette.Create(ThemeMode.Dark, ThemeAccent.Cyan);

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = palette.WindowBackColor;
        ClientSize = new Size(560, 300);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        ForeColor = palette.TextPrimaryColor;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = Padding.Empty;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        Text = "External API Notice";
        WindowChromeStyler.ApplyTheme(this, palette);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(30, 26, 30, 24),
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());
        root.BackColor = palette.WindowBackColor;

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold, GraphicsUnit.Point),
            ForeColor = palette.TextPrimaryColor,
            Margin = Padding.Empty,
            Text = "Before You Continue"
        };

        var body = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 16, 0, 0),
            Text = "Spectralis uses external APIs and services such as YouTube, SoundCloud, Suno, Spotify, and others.\r\n\r\nThese integrations are not always stable and may break in later releases.\r\n\r\nWould you like to proceed?",
            TextAlign = ContentAlignment.TopLeft
        };

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Padding(0, 22, 0, 0),
            WrapContents = false
        };
        buttons.BackColor = palette.WindowBackColor;

        var proceedButton = new ModernButton
        {
            Size = new Size(132, 40),
            Text = "Proceed"
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(proceedButton, palette, palette.AccentPrimaryColor);
        proceedButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Yes;
            Close();
        };

        var exitButton = new ModernButton
        {
            Margin = new Padding(0, 0, 10, 0),
            Size = new Size(104, 40),
            Text = "Exit"
        };
        ThemeControlStyler.ApplyGhostButtonTheme(exitButton, palette, palette.BorderStrongColor);
        exitButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.No;
            Close();
        };

        buttons.Controls.Add(proceedButton);
        buttons.Controls.Add(exitButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);
    }

    public static bool ConfirmProceed()
    {
        using var dialog = new ExternalApiConsentDialog();
        return dialog.ShowDialog() == DialogResult.Yes;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.Yes;
            Close();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.No;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }
}
