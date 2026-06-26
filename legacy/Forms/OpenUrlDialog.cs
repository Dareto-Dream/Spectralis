namespace Spectralis;

internal sealed class OpenUrlDialog : Form
{
    private readonly TextBox txtUrl;

    public OpenUrlDialog()
    {
        var settings = AppSettingsStore.Load();
        var palette = ThemePalette.Create(settings.ThemeMode, settings.ThemeAccent);

        Text = "Open URL";
        ClientSize = new Size(540, 130);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = false;
        Padding = new Padding(16);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;
        DoubleBuffered = true;
        WindowChromeStyler.ApplyTheme(this, palette);

        var label = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            ForeColor = palette.TextSecondaryColor,
            Location = new Point(16, 16),
            Text = "Paste a YouTube, SoundCloud, or Suno URL:"
        };

        txtUrl = new TextBox
        {
            Font = new Font("Segoe UI", 9F),
            Location = new Point(16, 40),
            Size = new Size(508, 24),
            PlaceholderText = "https://www.youtube.com/watch?v=... or https://soundcloud.com/... or https://suno.com/song/..."
        };
        txtUrl.BackColor = palette.SurfaceAltBackColor;
        txtUrl.ForeColor = palette.TextPrimaryColor;
        txtUrl.BorderStyle = BorderStyle.FixedSingle;

        var btnOpen = new ModernButton
        {
            Font = new Font("Segoe UI", 9F),
            Location = new Point(356, 86),
            Size = new Size(80, 32),
            Text = "Open"
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnOpen, palette, palette.AccentPrimaryColor);
        btnOpen.Click += (_, _) =>
        {
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new ModernButton
        {
            Font = new Font("Segoe UI", 9F),
            Location = new Point(444, 86),
            Size = new Size(80, 32),
            Text = "Cancel"
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btnCancel, palette, palette.BorderStrongColor);
        btnCancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };

        Controls.AddRange([label, txtUrl, btnOpen, btnCancel]);
    }

    public string Url => txtUrl.Text.Trim();

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        txtUrl.Focus();
        try
        {
            var clip = Clipboard.GetText().Trim();
            if (LooksLikeSupportedInput(clip))
            {
                txtUrl.Text = clip;
                txtUrl.SelectAll();
            }
        }
        catch { }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            DialogResult = DialogResult.OK;
            Close();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private static bool LooksLikeSupportedInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return value.Contains("youtube.com/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("youtu.be/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("soundcloud.com/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("on.soundcloud.com/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("snd.sc/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("suno.com/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("suno.ai/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("untitled.stream/", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("bandlab.com/", StringComparison.OrdinalIgnoreCase) ||
            SunoClipResolver.TryExtractClipId(value, out _);
    }
}
