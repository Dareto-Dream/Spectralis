using System.Drawing;

namespace Spectralis;

public sealed class UpdateProgressDialog : Form
{
    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;

    public UpdateProgressDialog(string? updateVersion)
    {
        var settings = AppSettingsStore.Load();
        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);
        var versionText = string.IsNullOrWhiteSpace(updateVersion)
            ? "Spectralis is updating."
            : $"Spectralis {updateVersion.Trim()} is updating.";

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(460, 190);
        ControlBox = false;
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = Padding.Empty;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Updating Spectralis";

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28, 24, 28, 24),
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = "Installing update"
        };

        var messageLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 12, 0, 18),
            Text = $"{versionText}{Environment.NewLine}Spectralis will keep running while the update installs."
        };

        statusLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 8),
            Text = "Preparing update..."
        };

        progressBar = new ProgressBar
        {
            Dock = DockStyle.Top,
            Height = 12,
            Margin = Padding.Empty,
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 28
        };

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(messageLabel, 0, 1);
        root.Controls.Add(statusLabel, 0, 2);
        root.Controls.Add(progressBar, 0, 3);
        Controls.Add(root);

        WindowChromeStyler.ApplyTheme(this, palette);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        root.BackColor = palette.WindowBackColor;
        titleLabel.ForeColor = palette.TextPrimaryColor;
        messageLabel.ForeColor = palette.TextSecondaryColor;
        statusLabel.ForeColor = palette.TextSoftColor;
    }

    public void SetStatus(string status)
    {
        statusLabel.Text = status;
        progressBar.MarqueeAnimationSpeed = 28;
        Refresh();
    }
}
