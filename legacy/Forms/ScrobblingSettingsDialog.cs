using System.Drawing;

namespace Spectralis;

internal sealed class ScrobblingSettingsDialog : Form
{
    private readonly AppSettings   _settings;
    private readonly ThemePalette  _theme;

    // Last.fm pending auth token (between "Connect" click and "Complete" click)
    private string? _lfmPendingToken;

    // ── Last.fm controls ──────────────────────────────────────────────────────
    private readonly CheckBox chkLfmEnabled   = new();
    private readonly TextBox  txtLfmApiKey    = new();
    private readonly TextBox  txtLfmApiSecret = new();
    private readonly Label    lblLfmStatus    = new();
    private readonly Button   btnLfmConnect   = new();
    private readonly Button   btnLfmComplete  = new();
    private readonly Button   btnLfmDisconnect = new();

    // ── ListenBrainz controls ─────────────────────────────────────────────────
    private readonly CheckBox chkLbzEnabled  = new();
    private readonly TextBox  txtLbzToken    = new();
    private readonly Label    lblLbzStatus   = new();
    private readonly Button   btnLbzVerify   = new();

    // ── Bottom ────────────────────────────────────────────────────────────────
    private readonly Button btnOk     = new();
    private readonly Button btnCancel = new();

    public ScrobblingSettingsDialog(AppSettings settings, ThemePalette theme)
    {
        _settings = settings.Clone();
        _theme    = theme;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Scrobbling Settings";
        FormBorderStyle     = FormBorderStyle.FixedDialog;
        StartPosition       = FormStartPosition.CenterParent;
        MaximizeBox         = false;
        MinimizeBox         = false;
        ClientSize          = new Size(480, 490);

        BuildLayout();
        PopulateFromSettings();
        ApplyTheme();
    }

    // ── Written-back settings after OK ───────────────────────────────────────
    public AppSettings Result => _settings;

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock      = DockStyle.Fill,
            RowCount  = 3,
            ColumnCount = 1,
            Margin    = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 195));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildLastFmPanel(), 0, 0);
        root.Controls.Add(BuildListenBrainzPanel(), 0, 1);
        root.Controls.Add(BuildBottomBar(), 0, 2);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private Panel BuildLastFmPanel()
    {
        var panel = new Panel
        {
            Dock    = DockStyle.Fill,
            Padding = new Padding(12, 32, 12, 10),
            Margin  = new Padding(8, 8, 8, 4),
        };

        var header = new Label
        {
            Text      = "Last.fm",
            AutoSize  = true,
            Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Location  = new Point(12, 8),
        };

        var sep = new Panel
        {
            Height   = 1,
            Dock     = DockStyle.Top,
            Margin   = Padding.Empty,
        };

        chkLfmEnabled.Text     = "Enable Last.fm scrobbling";
        chkLfmEnabled.AutoSize = true;
        chkLfmEnabled.Location = new Point(0, 0);

        var rowApiKey = MakeLabeledRow("API Key:", txtLfmApiKey, 70, 78, false);
        rowApiKey.Location = new Point(0, 24);
        rowApiKey.Anchor   = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        var rowSecret = MakeLabeledRow("API Secret:", txtLfmApiSecret, 70, 78, true);
        rowSecret.Location = new Point(0, 52);
        rowSecret.Anchor   = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        lblLfmStatus.Text     = "Not connected";
        lblLfmStatus.AutoSize = true;
        lblLfmStatus.Location = new Point(0, 86);

        btnLfmConnect.Text      = "Connect to Last.fm...";
        btnLfmConnect.AutoSize  = true;
        btnLfmConnect.FlatStyle = FlatStyle.Flat;
        btnLfmConnect.Location  = new Point(0, 108);
        btnLfmConnect.Click    += BtnLfmConnect_Click;

        btnLfmComplete.Text      = "Complete Authorization";
        btnLfmComplete.AutoSize  = true;
        btnLfmComplete.FlatStyle = FlatStyle.Flat;
        btnLfmComplete.Location  = new Point(160, 108);
        btnLfmComplete.Visible   = false;
        btnLfmComplete.Click    += BtnLfmComplete_Click;

        btnLfmDisconnect.Text      = "Disconnect";
        btnLfmDisconnect.AutoSize  = true;
        btnLfmDisconnect.FlatStyle = FlatStyle.Flat;
        btnLfmDisconnect.Location  = new Point(310, 108);
        btnLfmDisconnect.Click    += BtnLfmDisconnect_Click;

        var inner = new Panel { Dock = DockStyle.Fill };
        inner.Controls.AddRange([chkLfmEnabled, rowApiKey, rowSecret, lblLfmStatus,
                                  btnLfmConnect, btnLfmComplete, btnLfmDisconnect]);
        panel.Controls.Add(sep);
        panel.Controls.Add(inner);
        panel.Controls.Add(header);
        return panel;
    }

    private Panel BuildListenBrainzPanel()
    {
        var panel = new Panel
        {
            Dock    = DockStyle.Fill,
            Padding = new Padding(12, 32, 12, 10),
            Margin  = new Padding(8, 4, 8, 4),
        };

        var header = new Label
        {
            Text     = "ListenBrainz",
            AutoSize = true,
            Font     = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            Location = new Point(12, 8),
        };

        var sep = new Panel
        {
            Height = 1,
            Dock   = DockStyle.Top,
            Margin = Padding.Empty,
        };

        chkLbzEnabled.Text     = "Enable ListenBrainz scrobbling";
        chkLbzEnabled.AutoSize = true;
        chkLbzEnabled.Location = new Point(0, 0);

        var rowToken = MakeLabeledRow("User Token:", txtLbzToken, 70, 78, true);
        rowToken.Location = new Point(0, 26);
        rowToken.Anchor   = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;

        lblLbzStatus.Text     = "Not connected";
        lblLbzStatus.AutoSize = true;
        lblLbzStatus.Location = new Point(0, 56);

        btnLbzVerify.Text      = "Verify & Save";
        btnLbzVerify.AutoSize  = true;
        btnLbzVerify.FlatStyle = FlatStyle.Flat;
        btnLbzVerify.Location  = new Point(0, 78);
        btnLbzVerify.Click    += BtnLbzVerify_Click;

        var hint = new Label
        {
            Text     = "Get your token at listenbrainz.org/profile",
            AutoSize = true,
            Location = new Point(0, 112),
            Font     = new Font("Segoe UI", 8f),
        };

        var inner = new Panel { Dock = DockStyle.Fill };
        inner.Controls.AddRange([chkLbzEnabled, rowToken, lblLbzStatus, btnLbzVerify, hint]);
        panel.Controls.Add(sep);
        panel.Controls.Add(inner);
        panel.Controls.Add(header);
        return panel;
    }

    private Panel BuildBottomBar()
    {
        var panel = new Panel
        {
            Dock    = DockStyle.Fill,
            Padding = new Padding(8, 8, 8, 8),
        };

        btnOk.Text      = "OK";
        btnOk.Width     = 80;
        btnOk.Dock      = DockStyle.Right;
        btnOk.FlatStyle = FlatStyle.Flat;
        btnOk.Click    += (_, _) => CommitAndClose();

        btnCancel.Text      = "Cancel";
        btnCancel.Width     = 80;
        btnCancel.Dock      = DockStyle.Right;
        btnCancel.FlatStyle = FlatStyle.Flat;
        btnCancel.Click    += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        var spacer = new Panel { Dock = DockStyle.Right, Width = 6 };
        panel.Controls.Add(btnOk);
        panel.Controls.Add(spacer);
        panel.Controls.Add(btnCancel);
        return panel;
    }

    private static Panel MakeLabeledRow(string labelText, TextBox tb, int labelWidth, int tbLeft, bool password)
    {
        var panel = new Panel { Height = 24 };
        var lbl = new Label
        {
            Text      = labelText,
            Width     = labelWidth,
            Location  = new Point(0, 4),
            TextAlign = ContentAlignment.MiddleLeft,
        };
        tb.Location              = new Point(tbLeft, 2);
        tb.Width                 = 300;
        tb.Anchor                = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right;
        if (password) tb.UseSystemPasswordChar = true;
        panel.Controls.Add(lbl);
        panel.Controls.Add(tb);
        return panel;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Populate / theme
    // ─────────────────────────────────────────────────────────────────────────

    private void PopulateFromSettings()
    {
        chkLfmEnabled.Checked   = _settings.LastFmEnabled;
        txtLfmApiKey.Text        = _settings.LastFmApiKey;
        txtLfmApiSecret.Text     = _settings.LastFmApiSecret;

        chkLbzEnabled.Checked   = _settings.ListenBrainzEnabled;
        txtLbzToken.Text         = _settings.ListenBrainzToken;

        UpdateLfmStatus();
        UpdateLbzStatus();
    }

    private void UpdateLfmStatus()
    {
        var connected = !string.IsNullOrWhiteSpace(_settings.LastFmSessionKey);
        if (connected)
        {
            var name = string.IsNullOrWhiteSpace(_settings.LastFmUsername)
                ? "connected"
                : _settings.LastFmUsername;
            lblLfmStatus.Text    = $"Connected as {name}";
            lblLfmStatus.ForeColor = _theme.AccentPrimaryColor;
        }
        else
        {
            lblLfmStatus.Text    = "Not connected";
            lblLfmStatus.ForeColor = _theme.TextMutedColor;
        }
        btnLfmDisconnect.Enabled = connected;
        btnLfmComplete.Visible   = _lfmPendingToken is not null && !connected;
    }

    private void UpdateLbzStatus()
    {
        var connected = !string.IsNullOrWhiteSpace(_settings.ListenBrainzToken) &&
                        !string.IsNullOrWhiteSpace(_settings.ListenBrainzUsername);
        if (connected)
        {
            lblLbzStatus.Text     = $"Verified as {_settings.ListenBrainzUsername}";
            lblLbzStatus.ForeColor = _theme.AccentPrimaryColor;
        }
        else
        {
            lblLbzStatus.Text     = "Not connected";
            lblLbzStatus.ForeColor = _theme.TextMutedColor;
        }
    }

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;

        foreach (var c in GetAllChildren(this))
        {
            switch (c)
            {
                case Panel p when p.Height == 1:
                    p.BackColor = _theme.BorderStrongColor;
                    break;
                case Button btn:
                    btn.BackColor = _theme.SurfaceRaisedColor;
                    btn.ForeColor = _theme.TextSecondaryColor;
                    btn.FlatAppearance.BorderColor = _theme.BorderStrongColor;
                    break;
                case TextBox tb:
                    tb.BackColor = _theme.SurfaceAltBackColor;
                    tb.ForeColor = _theme.TextPrimaryColor;
                    break;
                case CheckBox cb:
                    cb.BackColor  = Color.Transparent;
                    cb.ForeColor  = _theme.TextPrimaryColor;
                    cb.FlatStyle  = FlatStyle.Flat;
                    break;
                case Label lbl when lbl != lblLfmStatus && lbl != lblLbzStatus:
                    lbl.ForeColor = _theme.TextSecondaryColor;
                    break;
            }
        }

        UpdateLfmStatus();
        UpdateLbzStatus();
    }

    private static IEnumerable<Control> GetAllChildren(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (var child in GetAllChildren(c))
                yield return child;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private async void BtnLfmConnect_Click(object? sender, EventArgs e)
    {
        var apiKey    = txtLfmApiKey.Text.Trim();
        var apiSecret = txtLfmApiSecret.Text.Trim();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
        {
            MessageBox.Show(this,
                "Enter your Last.fm API Key and API Secret first.",
                "Last.fm Connect",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnLfmConnect.Enabled = false;
        lblLfmStatus.Text = "Requesting token...";

        try
        {
            var token = await LastFmClient.GetTokenAsync(apiKey, apiSecret);
            if (token is null)
            {
                lblLfmStatus.Text = "Failed to get token.";
                btnLfmConnect.Enabled = true;
                return;
            }

            _lfmPendingToken = token;

            // Open browser for user authorization
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(apiKey)}&token={Uri.EscapeDataString(token)}",
                UseShellExecute = true,
            });

            lblLfmStatus.Text           = "Authorize in browser, then click Complete.";
            btnLfmComplete.Visible      = true;
            btnLfmConnect.Enabled       = true;
        }
        catch (Exception ex)
        {
            lblLfmStatus.Text     = $"Error: {ex.Message}";
            btnLfmConnect.Enabled = true;
        }
    }

    private async void BtnLfmComplete_Click(object? sender, EventArgs e)
    {
        if (_lfmPendingToken is null) return;

        var apiKey    = txtLfmApiKey.Text.Trim();
        var apiSecret = txtLfmApiSecret.Text.Trim();
        btnLfmComplete.Enabled = false;
        lblLfmStatus.Text = "Completing authorization...";

        try
        {
            var (sessionKey, username) = await LastFmClient.GetSessionAsync(apiKey, apiSecret, _lfmPendingToken);
            if (sessionKey is null)
            {
                lblLfmStatus.Text      = "Authorization not granted yet. Try again after authorizing.";
                btnLfmComplete.Enabled = true;
                return;
            }

            _settings.LastFmSessionKey = sessionKey;
            _settings.LastFmUsername   = username ?? "";
            _settings.LastFmApiKey     = apiKey;
            _settings.LastFmApiSecret  = apiSecret;
            _lfmPendingToken           = null;
            btnLfmComplete.Enabled     = true;
            UpdateLfmStatus();
        }
        catch (Exception ex)
        {
            lblLfmStatus.Text      = $"Error: {ex.Message}";
            btnLfmComplete.Enabled = true;
        }
    }

    private void BtnLfmDisconnect_Click(object? sender, EventArgs e)
    {
        _settings.LastFmSessionKey = "";
        _settings.LastFmUsername   = "";
        _lfmPendingToken           = null;
        UpdateLfmStatus();
    }

    private async void BtnLbzVerify_Click(object? sender, EventArgs e)
    {
        var token = txtLbzToken.Text.Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            MessageBox.Show(this,
                "Enter your ListenBrainz user token first.",
                "ListenBrainz",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnLbzVerify.Enabled = false;
        lblLbzStatus.Text    = "Verifying...";

        try
        {
            var client   = new ListenBrainzClient(token);
            var valid    = await client.ValidateTokenAsync();
            if (!valid)
            {
                lblLbzStatus.Text     = "Token invalid.";
                lblLbzStatus.ForeColor = _theme.DangerColor;
                btnLbzVerify.Enabled  = true;
                return;
            }
            var username = await client.GetUsernameAsync() ?? "";
            _settings.ListenBrainzToken    = token;
            _settings.ListenBrainzUsername = username;
            UpdateLbzStatus();
        }
        catch
        {
            lblLbzStatus.Text    = "Could not connect.";
            lblLbzStatus.ForeColor = _theme.DangerColor;
        }

        btnLbzVerify.Enabled = true;
    }

    private void CommitAndClose()
    {
        _settings.LastFmEnabled        = chkLfmEnabled.Checked;
        _settings.LastFmApiKey         = txtLfmApiKey.Text.Trim();
        _settings.LastFmApiSecret      = txtLfmApiSecret.Text.Trim();
        _settings.ListenBrainzEnabled  = chkLbzEnabled.Checked;
        _settings.ListenBrainzToken    = txtLbzToken.Text.Trim();
        DialogResult = DialogResult.OK;
        Close();
    }
}
