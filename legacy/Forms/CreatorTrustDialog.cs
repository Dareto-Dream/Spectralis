using System.Drawing;

namespace Spectralis;

internal sealed class CreatorTrustDialog : Form
{
    public bool Trusted { get; private set; }

    public CreatorTrustDialog(CreatorKeyMetadata key, AppSettings settings)
    {
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(480, 340);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Trust Creator";

        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, palette);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var lblHeading = new Label
        {
            Text = "Trust this creator?",
            Font = new Font(Font.FontFamily, 13f, FontStyle.Bold),
            AutoSize = true,
            ForeColor = palette.TextPrimaryColor
        };
        layout.Controls.Add(lblHeading);

        var lblCreator = new Label
        {
            Text = key.DisplayName,
            Font = new Font(Font.FontFamily, 11f),
            AutoSize = true,
            ForeColor = palette.AccentPrimaryColor,
            Margin = new Padding(0, 8, 0, 0)
        };
        layout.Controls.Add(lblCreator);

        var capsNormalized = key.AllowedCapabilities.Count > 0
            ? string.Join(", ", key.AllowedCapabilities)
            : "none";

        var lblInfo = new Label
        {
            Text = $"This creator's capsule requires permission to run on your device.\n\n" +
                   $"Allowed capabilities: {capsNormalized}\n\n" +
                   $"Only trust creators you recognize. Once trusted, future capsules from this creator will open automatically.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = palette.TextSecondaryColor,
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(lblInfo);

        if (!string.IsNullOrWhiteSpace(key.ProfileUrl))
        {
            var lblUrl = new Label
            {
                Text = key.ProfileUrl,
                AutoSize = true,
                ForeColor = palette.AccentSoftColor,
                Cursor = Cursors.Hand
            };
            lblUrl.Click += (_, _) =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(key.ProfileUrl) { UseShellExecute = true }); }
                catch { }
            };
            layout.Controls.Add(lblUrl);
        }
        else
        {
            layout.Controls.Add(new Label { Height = 1 });
        }

        var btnPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        layout.Controls.Add(btnPanel);

        var btnTrust = new ModernButton
        {
            Text = "Trust Creator",
            Size = new Size(130, 36),
            Margin = new Padding(8, 0, 0, 0)
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnTrust, palette, palette.AccentPrimaryColor);
        btnTrust.Click += (_, _) => { Trusted = true; DialogResult = DialogResult.OK; Close(); };

        var btnCancel = new ModernButton
        {
            Text = "Cancel",
            Size = new Size(90, 36)
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btnCancel, palette, palette.BorderStrongColor);
        btnCancel.Click += (_, _) => { Trusted = false; DialogResult = DialogResult.Cancel; Close(); };

        btnPanel.Controls.Add(btnTrust);
        btnPanel.Controls.Add(btnCancel);
    }
}
