using System.Drawing;

namespace Spectralis;

internal sealed class EffectsChainDialog : Form
{
    private readonly EffectChain  _chain;
    private readonly ThemePalette _theme;

    private readonly EffectsChainControl _control;
    private readonly Button btnClose = new();

    public EffectsChainDialog(EffectChain chain, ThemePalette theme)
    {
        _chain = chain;
        _theme = theme;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Effects Chain";
        FormBorderStyle     = FormBorderStyle.SizableToolWindow;
        StartPosition       = FormStartPosition.CenterParent;
        ShowInTaskbar       = false;
        ClientSize          = new Size(560, 280);
        MinimumSize     = new Size(480, 260);

        _control = new EffectsChainControl(chain, theme)
        {
            Dock = DockStyle.Fill,
        };

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44 };
        btnClose.Text      = "Close";
        btnClose.Width     = 80;
        btnClose.Dock      = DockStyle.Right;
        btnClose.FlatStyle = FlatStyle.Flat;
        btnClose.Click    += (_, _) => Close();
        bar.Controls.Add(btnClose);

        Controls.Add(_control);
        Controls.Add(bar);

        ApplyTheme();
    }

    private void ApplyTheme()
    {
        BackColor = _theme.WindowBackColor;
        ForeColor = _theme.TextPrimaryColor;

        btnClose.BackColor = _theme.SurfaceRaisedColor;
        btnClose.ForeColor = _theme.TextSecondaryColor;
        btnClose.FlatAppearance.BorderColor = _theme.BorderStrongColor;
    }
}
