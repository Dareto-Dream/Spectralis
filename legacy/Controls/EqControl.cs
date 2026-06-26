using System.Drawing;

namespace Spectralis;

internal sealed class EqControl : UserControl
{
    private readonly Eq10BandEffect _effect;
    private readonly TrackBar[]     _bands = new TrackBar[10];
    private readonly Label[]        _labels = new Label[10];
    private readonly NumericUpDown  _preamp = new();
    private bool _updating;

    public EqControl(Eq10BandEffect effect)
    {
        _effect = effect;
        Height  = 120;
        BuildLayout();
        Reload();
    }

    private void BuildLayout()
    {
        SuspendLayout();

        var labels = new[] { "31", "62", "125", "250", "500", "1K", "2K", "4K", "8K", "16K" };
        var colW   = 40;

        for (var i = 0; i < 10; i++)
        {
            var idx = i;

            var tb = new TrackBar
            {
                Orientation   = Orientation.Vertical,
                Minimum       = -120,  // -12.0 dB
                Maximum       =  120,  //  12.0 dB
                TickFrequency = 10,
                Width         = 24,
                Height        = 70,
                Location      = new Point(i * colW + 8, 4),
            };
            tb.ValueChanged += (_, _) =>
            {
                if (_updating) return;
                _effect.Parameters.Set($"band{idx}", tb.Value / 10f);
                _labels[idx].Text = FormatGain(tb.Value / 10f);
                Changed?.Invoke(this, EventArgs.Empty);
            };

            var lbl = new Label
            {
                Text      = "",
                Font      = new Font("Segoe UI", 7f),
                Width     = 34,
                Height    = 14,
                Location  = new Point(i * colW + 2, 76),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            var freqLbl = new Label
            {
                Text      = labels[i],
                Font      = new Font("Segoe UI", 7f),
                Width     = 34,
                Height    = 14,
                Location  = new Point(i * colW + 2, 90),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            _bands[i]  = tb;
            _labels[i] = lbl;
            Controls.Add(tb);
            Controls.Add(lbl);
            Controls.Add(freqLbl);
        }

        // Preamp
        var preampLabel = new Label
        {
            Text      = "Pre",
            Font      = new Font("Segoe UI", 7.5f),
            Width     = 24,
            Height    = 14,
            Location  = new Point(410, 4),
            TextAlign = ContentAlignment.MiddleCenter,
        };
        _preamp.Minimum       = -12;
        _preamp.Maximum       =  12;
        _preamp.DecimalPlaces = 1;
        _preamp.Increment     = (decimal)0.5;
        _preamp.Width         = 54;
        _preamp.Location      = new Point(402, 20);
        _preamp.Font          = new Font("Segoe UI", 8f);
        _preamp.ValueChanged  += (_, _) =>
        {
            if (_updating) return;
            _effect.Parameters.Set("preamp", (float)_preamp.Value);
            Changed?.Invoke(this, EventArgs.Empty);
        };

        Controls.Add(preampLabel);
        Controls.Add(_preamp);
        Width = 470;

        ResumeLayout(false);
    }

    public event EventHandler? Changed;

    public void Reload()
    {
        _updating = true;
        for (var i = 0; i < 10; i++)
        {
            var gain  = _effect.Parameters.Get($"band{i}", 0f);
            _bands[i].Value  = (int)Math.Round(gain * 10);
            _labels[i].Text  = FormatGain(gain);
        }
        _preamp.Value = (decimal)_effect.Parameters.Get("preamp", 0f);
        _updating = false;
    }

    private static string FormatGain(float db)
    {
        if (Math.Abs(db) < 0.05f) return "0";
        return db > 0 ? $"+{db:F1}" : $"{db:F1}";
    }
}
