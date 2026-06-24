using System.Drawing;

namespace Spectralis;

internal sealed class RedeemVisualizerDialog : Form
{
    private readonly ThemePalette palette;
    private readonly RedeemableVisualizerService redeemableVisualizers;
    private readonly TextBox txtRedeemKey;
    private readonly ModernButton btnRedeem;
    private readonly ModernButton btnClose;
    private readonly Label lblRedeemStatus;
    private readonly Label lblInstalledCount;

    public RedeemVisualizerDialog(ThemePalette palette, RedeemableVisualizerService redeemableVisualizers)
    {
        this.palette = palette;
        this.redeemableVisualizers = redeemableVisualizers;

        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(520, 270);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Padding = new Padding(22);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Redeem Visualizer";

        txtRedeemKey = new TextBox
        {
            Font = new Font("Segoe UI", 9F),
            Margin = Padding.Empty,
            MaxLength = 64,
            PlaceholderText = "Redeem key"
        };
        txtRedeemKey.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
                return;

            e.SuppressKeyPress = true;
            BtnRedeem_Click(this, EventArgs.Empty);
        };

        btnRedeem = new ModernButton
        {
            Size = new Size(112, 36),
            Text = "Redeem"
        };
        btnRedeem.Click += BtnRedeem_Click;

        btnClose = new ModernButton
        {
            Size = new Size(112, 38),
            Text = "Close"
        };
        btnClose.Click += (_, _) => Close();

        lblRedeemStatus = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.75F),
            Margin = new Padding(0, 10, 0, 0),
            MaximumSize = new Size(450, 0),
            Text = "Enter a code to unlock a special visualizer."
        };

        lblInstalledCount = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 8.25F),
            Margin = new Padding(0, 4, 0, 0),
            MaximumSize = new Size(450, 0),
            Text = BuildInstalledText()
        };

        Controls.Add(CreateLayout());
        ApplyTheme();
    }

    public bool InstalledVisualizersChanged { get; private set; }

    public string? RedeemedVisualizerId { get; private set; }

    private Control CreateLayout()
    {
        var layout = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle());
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle());

        var title = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 0, 10),
            Text = "Redeem Visualizer"
        };

        var body = new Label
        {
            AutoSize = true,
            Font = new Font("Segoe UI", 9.25F),
            Margin = new Padding(0, 0, 0, 12),
            MaximumSize = new Size(450, 0),
            Text = "Unlock CDN-hosted visualizers and keep local copies for offline playback."
        };

        var row = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 6, 0, 0),
            Padding = Padding.Empty,
            RowCount = 1,
            Height = 38
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124F));
        txtRedeemKey.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtRedeemKey.Margin = new Padding(0, 4, 10, 0);
        btnRedeem.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        btnRedeem.Margin = Padding.Empty;
        row.Controls.Add(txtRedeemKey, 0, 0);
        row.Controls.Add(btnRedeem, 1, 0);

        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            WrapContents = false
        };
        btnClose.Margin = Padding.Empty;
        footer.Controls.Add(btnClose);

        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(body, 0, 1);
        layout.Controls.Add(row, 0, 2);
        layout.Controls.Add(lblRedeemStatus, 0, 3);
        layout.Controls.Add(lblInstalledCount, 0, 4);
        layout.Controls.Add(footer, 0, 5);
        return layout;
    }

    private async void BtnRedeem_Click(object? sender, EventArgs e)
    {
        var redeemKey = txtRedeemKey.Text.Trim();
        if (string.IsNullOrWhiteSpace(redeemKey))
        {
            lblRedeemStatus.Text = "Enter a key first.";
            return;
        }

        btnRedeem.Enabled = false;
        btnRedeem.Text = "Checking...";
        lblRedeemStatus.Text = "Contacting Spectralis CDN...";

        try
        {
            var visualizer = await redeemableVisualizers.RedeemAsync(redeemKey, CancellationToken.None);
            InstalledVisualizersChanged = true;
            RedeemedVisualizerId = visualizer.Id;
            txtRedeemKey.Clear();
            lblRedeemStatus.Text = $"Unlocked: {visualizer.DisplayName}";
            lblInstalledCount.Text = BuildInstalledText();
        }
        catch (Exception ex)
        {
            lblRedeemStatus.Text = "Redeem failed.";
            MessageBox.Show(
                this,
                ex.Message,
                "Redeem Visualizer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            btnRedeem.Text = "Redeem";
            btnRedeem.Enabled = true;
        }
    }

    private string BuildInstalledText()
    {
        var count = redeemableVisualizers.Installed.Count;
        return $"{count} special visualizer{(count == 1 ? "" : "s")} installed.";
    }

    private void ApplyTheme()
    {
        WindowChromeStyler.ApplyTheme(this, palette);
        BackColor = palette.WindowBackColor;
        ForeColor = palette.TextPrimaryColor;

        foreach (Control control in GetAllControls(this))
        {
            control.BackColor = palette.WindowBackColor;
            if (control is Label label)
            {
                label.ForeColor = label == lblRedeemStatus || label == lblInstalledCount
                    ? palette.TextMutedColor
                    : palette.TextPrimaryColor;
            }
        }

        txtRedeemKey.BackColor = palette.SurfaceAltBackColor;
        txtRedeemKey.ForeColor = palette.TextPrimaryColor;
        txtRedeemKey.BorderStyle = BorderStyle.FixedSingle;
        ThemeControlStyler.ApplyPrimaryButtonTheme(btnRedeem, palette, palette.AccentPrimaryColor);
        ThemeControlStyler.ApplyGhostButtonTheme(btnClose, palette, palette.BorderStrongColor);
    }

    private static IEnumerable<Control> GetAllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in GetAllControls(child))
                yield return descendant;
        }
    }
}
