using System.Drawing;

namespace Spectralis;

internal sealed class EffectsChainControl : UserControl
{
    private readonly EffectChain _chain;
    private readonly ThemePalette _theme;

    // ── Header ────────────────────────────────────────────────────────────────
    private readonly CheckBox  chkEnabled    = new();
    private readonly ComboBox  cmbAddEffect  = new();
    private readonly Button    btnAdd        = new();
    private readonly Button    btnRemove     = new();
    private readonly Button    btnMoveUp     = new();
    private readonly Button    btnMoveDown   = new();

    // ── Effect list ───────────────────────────────────────────────────────────
    private readonly ListBox lstEffects = new();

    // ── Params panel ─────────────────────────────────────────────────────────
    private readonly Panel paramsPanel = new();

    public event EventHandler? ChainChanged;

    public EffectsChainControl(EffectChain chain, ThemePalette theme)
    {
        _chain = chain;
        _theme = theme;
        DoubleBuffered = true;
        BuildLayout();
        ApplyTheme();
        Reload();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        // ── Top toolbar ──────────────────────────────────────────────────────
        chkEnabled.Text     = "Effects Chain";
        chkEnabled.Checked  = _chain.Enabled;
        chkEnabled.Location = new Point(6, 8);
        chkEnabled.AutoSize = true;
        chkEnabled.Font     = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        chkEnabled.CheckedChanged += (_, _) =>
        {
            _chain.Enabled = chkEnabled.Checked;
            FireChainChanged();
        };

        cmbAddEffect.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbAddEffect.Width         = 110;
        cmbAddEffect.Location      = new Point(6, 36);
        cmbAddEffect.Font          = new Font("Segoe UI", 9f);
        cmbAddEffect.Items.AddRange(EffectChain.AvailableEffects);
        cmbAddEffect.SelectedIndex = 0;

        btnAdd.Text      = "Add";
        btnAdd.Width     = 48;
        btnAdd.Location  = new Point(120, 35);
        btnAdd.FlatStyle = FlatStyle.Flat;
        btnAdd.Click    += (_, _) =>
        {
            if (cmbAddEffect.SelectedItem is not string name) return;
            var effect = EffectChain.CreateEffect(name);
            _chain.Add(effect);
            Reload();
            lstEffects.SelectedIndex = lstEffects.Items.Count - 1;
            FireChainChanged();
        };

        btnRemove.Text     = "Remove";
        btnRemove.Width    = 56;
        btnRemove.Location = new Point(174, 35);
        btnRemove.FlatStyle = FlatStyle.Flat;
        btnRemove.Click   += (_, _) =>
        {
            var idx = lstEffects.SelectedIndex;
            if (idx < 0 || idx >= _chain.Effects.Count) return;
            _chain.Remove(_chain.Effects[idx]);
            Reload();
            if (lstEffects.Items.Count > 0)
                lstEffects.SelectedIndex = Math.Min(idx, lstEffects.Items.Count - 1);
            FireChainChanged();
        };

        btnMoveUp.Text     = "▲";
        btnMoveUp.Width    = 28;
        btnMoveUp.Location = new Point(236, 35);
        btnMoveUp.FlatStyle = FlatStyle.Flat;
        btnMoveUp.Click   += (_, _) =>
        {
            var idx = lstEffects.SelectedIndex;
            _chain.MoveUp(idx);
            Reload();
            if (idx > 0) lstEffects.SelectedIndex = idx - 1;
            FireChainChanged();
        };

        btnMoveDown.Text     = "▼";
        btnMoveDown.Width    = 28;
        btnMoveDown.Location = new Point(268, 35);
        btnMoveDown.FlatStyle = FlatStyle.Flat;
        btnMoveDown.Click   += (_, _) =>
        {
            var idx = lstEffects.SelectedIndex;
            _chain.MoveDown(idx);
            Reload();
            if (idx < lstEffects.Items.Count - 1) lstEffects.SelectedIndex = idx + 1;
            FireChainChanged();
        };

        // ── Effect list ──────────────────────────────────────────────────────
        lstEffects.Location      = new Point(6, 68);
        lstEffects.Size          = new Size(200, 140);
        lstEffects.Font          = new Font("Segoe UI", 9f);
        lstEffects.BorderStyle   = BorderStyle.FixedSingle;
        lstEffects.SelectedIndexChanged += LstEffects_SelectedIndexChanged;

        // ── Params panel ─────────────────────────────────────────────────────
        paramsPanel.Location = new Point(212, 68);
        paramsPanel.Size     = new Size(320, 140);
        paramsPanel.BorderStyle = BorderStyle.None;

        Controls.AddRange([chkEnabled, cmbAddEffect, btnAdd, btnRemove, btnMoveUp, btnMoveDown,
                           lstEffects, paramsPanel]);

        Height = 220;
        ResumeLayout(false);
    }

    private void LstEffects_SelectedIndexChanged(object? sender, EventArgs e)
    {
        UpdateParamsPanel();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void Reload()
    {
        lstEffects.Items.Clear();
        foreach (var effect in _chain.Effects)
            lstEffects.Items.Add($"{(effect.Enabled ? "✓" : "○")} {effect.Name}");
    }

    private void UpdateParamsPanel()
    {
        paramsPanel.Controls.Clear();

        var idx = lstEffects.SelectedIndex;
        if (idx < 0 || idx >= _chain.Effects.Count) return;

        var effect = _chain.Effects[idx];

        // Bypass checkbox
        var chkBypass = new CheckBox
        {
            Text      = "Enabled",
            Checked   = effect.Enabled,
            AutoSize  = true,
            Location  = new Point(0, 0),
            ForeColor = _theme.TextSecondaryColor,
        };
        chkBypass.CheckedChanged += (_, _) =>
        {
            effect.Enabled = chkBypass.Checked;
            Reload();
            lstEffects.SelectedIndex = idx;
            FireChainChanged();
        };
        paramsPanel.Controls.Add(chkBypass);

        // Effect-specific controls
        Control? paramCtrl = effect switch
        {
            Eq10BandEffect eq =>
                MakeEqPanel(eq),
            CompressorEffect comp =>
                MakeCompressorPanel(comp),
            ReverbEffect rev =>
                MakeReverbPanel(rev),
            VocalBlendEffect vocal =>
                MakeVocalPanel(vocal),
            _ => null,
        };

        if (paramCtrl is not null)
        {
            paramCtrl.Location = new Point(0, 24);
            paramsPanel.Controls.Add(paramCtrl);
        }
    }

    private Control MakeEqPanel(Eq10BandEffect eq)
    {
        var ctrl = new EqControl(eq);
        ctrl.Changed += (_, _) => FireChainChanged();
        return ctrl;
    }

    private Control MakeCompressorPanel(CompressorEffect comp)
    {
        var panel = new TableLayoutPanel { RowCount = 5, ColumnCount = 2, AutoSize = true };

        void AddRow(string label, string key, float min, float max, float step)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 8.5f) });
            var nud = new NumericUpDown
            {
                Minimum       = (decimal)min,
                Maximum       = (decimal)max,
                DecimalPlaces = 1,
                Increment     = (decimal)step,
                Value         = (decimal)comp.Parameters.Get(key, 0),
                Width         = 70,
            };
            nud.ValueChanged += (_, _) =>
            {
                comp.Parameters.Set(key, (float)nud.Value);
                FireChainChanged();
            };
            panel.Controls.Add(nud);
        }

        AddRow("Threshold (dBFS)", "threshold", -60, 0, 1);
        AddRow("Ratio", "ratio", 1, 20, 0.5f);
        AddRow("Attack (ms)", "attack", 0.1f, 500, 1);
        AddRow("Release (ms)", "release", 10, 5000, 10);
        AddRow("Makeup (dB)", "makeup", -12, 24, 0.5f);
        return panel;
    }

    private Control MakeReverbPanel(ReverbEffect rev)
    {
        var panel = new Panel { AutoSize = true };

        void AddSlider(string label, string key, float min, float max, int y)
        {
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(0, y), Font = new Font("Segoe UI", 8.5f) });
            var tb = new TrackBar
            {
                Minimum      = (int)(min * 100),
                Maximum      = (int)(max * 100),
                Value        = (int)(rev.Parameters.Get(key, 0.5f) * 100),
                Width        = 160,
                TickFrequency = 10,
                Location     = new Point(90, y - 4),
            };
            tb.ValueChanged += (_, _) =>
            {
                rev.Parameters.Set(key, tb.Value / 100f);
                FireChainChanged();
            };
            panel.Controls.Add(tb);
        }

        AddSlider("Room Size", "roomSize", 0, 1, 0);
        AddSlider("Damping",   "damping",  0, 1, 32);
        AddSlider("Wet",       "wet",      0, 1, 64);
        return panel;
    }

    private Control MakeVocalPanel(VocalBlendEffect vocal)
    {
        var panel = new Panel { AutoSize = true };
        panel.Controls.Add(new Label { Text = "Vocal Remove", AutoSize = true, Location = new Point(0, 8), Font = new Font("Segoe UI", 8.5f) });
        var tb = new TrackBar
        {
            Minimum      = 0,
            Maximum      = 100,
            Value        = (int)(vocal.Parameters.Get("blend", 0.5f) * 100),
            Width        = 160,
            Location     = new Point(100, 4),
        };
        tb.ValueChanged += (_, _) =>
        {
            vocal.Parameters.Set("blend", tb.Value / 100f);
            FireChainChanged();
        };
        panel.Controls.Add(tb);
        return panel;
    }

    private void FireChainChanged()
    {
        _chain.NotifyChanged();
        ChainChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;

        chkEnabled.ForeColor  = _theme.AccentPrimaryColor;
        lstEffects.BackColor  = _theme.SurfaceAltBackColor;
        lstEffects.ForeColor  = _theme.TextPrimaryColor;
        paramsPanel.BackColor = _theme.WindowBackColor;

        foreach (var btn in new[] { btnAdd, btnRemove, btnMoveUp, btnMoveDown })
        {
            btn.BackColor = _theme.SurfaceRaisedColor;
            btn.ForeColor = _theme.TextSecondaryColor;
            btn.FlatAppearance.BorderColor = _theme.BorderStrongColor;
        }

        cmbAddEffect.BackColor = _theme.SurfaceAltBackColor;
        cmbAddEffect.ForeColor = _theme.TextPrimaryColor;
    }
}
