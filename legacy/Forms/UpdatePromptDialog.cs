using System.Drawing;

namespace Spectralis;

public enum UpdatePromptChoice
{
    RemindLater,
    UpdateNow,
    DontRemindAgain
}

public sealed class UpdatePromptDialog : Form
{
    public UpdatePromptChoice Choice { get; private set; } = UpdatePromptChoice.RemindLater;

    public UpdatePromptDialog(string? updateVersion)
    {
        var settings = AppSettingsStore.Load();
        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);
        var versionText = string.IsNullOrWhiteSpace(updateVersion)
            ? "A newer version of Spectralis is available."
            : $"Spectralis {updateVersion.Trim()} is available.";

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(500, 230);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = Padding.Empty;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Spectralis Update";

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(28, 24, 28, 22),
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle());
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle());

        var titleLabel = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = Padding.Empty,
            Text = "A Spectralis update is available"
        };

        var messageLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9.25F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 14, 0, 18),
            Text = $"{versionText}{Environment.NewLine}{Environment.NewLine}Would you like to install it now?"
        };

        var buttons = new TableLayoutPanel
        {
            ColumnCount = 4,
            Dock = DockStyle.Fill,
            Height = 42,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 1
        };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var dontRemindButton = CreateButton("Don't remind again", 168);
        dontRemindButton.Click += (_, _) => CloseWith(UpdatePromptChoice.DontRemindAgain);

        var remindLaterButton = CreateButton("Remind later", 126);
        remindLaterButton.Click += (_, _) => CloseWith(UpdatePromptChoice.RemindLater);

        var updateButton = CreateButton("Update now", 126);
        updateButton.Click += (_, _) => CloseWith(UpdatePromptChoice.UpdateNow);

        buttons.Controls.Add(dontRemindButton, 1, 0);
        buttons.Controls.Add(remindLaterButton, 2, 0);
        buttons.Controls.Add(updateButton, 3, 0);

        root.Controls.Add(titleLabel, 0, 0);
        root.Controls.Add(messageLabel, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        Controls.Add(root);

        WindowChromeStyler.ApplyTheme(this, palette);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        root.BackColor = palette.WindowBackColor;
        buttons.BackColor = palette.WindowBackColor;
        titleLabel.ForeColor = palette.TextPrimaryColor;
        messageLabel.ForeColor = palette.TextSecondaryColor;
        ThemeControlStyler.ApplyGhostButtonTheme(dontRemindButton, palette, palette.BorderStrongColor);
        ThemeControlStyler.ApplyGhostButtonTheme(remindLaterButton, palette, palette.AccentSoftColor);
        ThemeControlStyler.ApplyPrimaryButtonTheme(updateButton, palette, palette.AccentPrimaryColor);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            CloseWith(UpdatePromptChoice.RemindLater);
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static ModernButton CreateButton(string text, int width) =>
        new()
        {
            Margin = new Padding(10, 0, 0, 0),
            Size = new Size(width, 42),
            Text = text
        };

    private void CloseWith(UpdatePromptChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
