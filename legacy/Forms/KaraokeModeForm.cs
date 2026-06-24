using System.Drawing;

namespace Spectralis;

internal sealed class KaraokeModeForm : Form
{
    private readonly KaraokeDisplayControl _display;
    private readonly Label _lblTime;
    private readonly TrackBar _tbBlend;
    private readonly Action _togglePlayback;
    private readonly Action<float> _onVocalBlend;

    public KaraokeModeForm(Action togglePlayback, Action<float> onVocalBlend)
    {
        _togglePlayback = togglePlayback;
        _onVocalBlend   = onVocalBlend;

        Text            = "Karaoke Mode — Spectralis";
        FormBorderStyle = FormBorderStyle.None;
        WindowState     = FormWindowState.Maximized;
        BackColor       = Color.Black;
        ShowInTaskbar   = false;
        TopMost         = true;
        KeyPreview      = true;
        DoubleBuffered  = true;

        _display = new KaraokeDisplayControl { Dock = DockStyle.Fill };

        // ── Bottom status bar ────────────────────────────────────────────────
        var bar = new Panel
        {
            Dock      = DockStyle.Bottom,
            Height    = 52,
            BackColor = Color.FromArgb(22, 22, 22),
        };

        _lblTime = new Label
        {
            AutoSize  = false,
            Width     = 72,
            Dock      = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(140, 255, 255, 255),
            Font      = new Font("Segoe UI", 10f),
        };

        var lblBlend = new Label
        {
            Text      = "Vocal Remove",
            AutoSize  = false,
            Width     = 110,
            Dock      = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(140, 255, 255, 255),
            Font      = new Font("Segoe UI", 9f),
            Padding   = new Padding(0, 0, 6, 0),
        };

        _tbBlend = new TrackBar
        {
            Minimum       = 0,
            Maximum       = 100,
            Value         = 0,
            Width         = 180,
            Dock          = DockStyle.Left,
            BackColor     = Color.FromArgb(22, 22, 22),
            TickFrequency = 25,
        };
        _tbBlend.ValueChanged += (_, _) => _onVocalBlend(_tbBlend.Value / 100f);

        var lblBlendVal = new Label
        {
            AutoSize  = false,
            Width     = 44,
            Dock      = DockStyle.Left,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(140, 255, 255, 255),
            Font      = new Font("Segoe UI", 9f),
            Text      = "0%",
        };
        _tbBlend.ValueChanged += (_, _) => lblBlendVal.Text = $"{_tbBlend.Value}%";

        var btnClose = new Button
        {
            Text      = "✕  Exit",
            Width     = 88,
            Dock      = DockStyle.Right,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.FromArgb(200, 255, 255, 255),
            Font      = new Font("Segoe UI", 9.5f),
            Cursor    = Cursors.Hand,
        };
        btnClose.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        btnClose.Click += (_, _) => Close();

        var lblHint = new Label
        {
            Text      = "Space = play/pause   Esc = exit",
            AutoSize  = false,
            Dock      = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(55, 255, 255, 255),
            Font      = new Font("Segoe UI", 8.5f),
        };

        bar.Controls.Add(lblHint);
        bar.Controls.Add(lblBlendVal);
        bar.Controls.Add(_tbBlend);
        bar.Controls.Add(lblBlend);
        bar.Controls.Add(_lblTime);
        bar.Controls.Add(btnClose);

        Controls.Add(_display);
        Controls.Add(bar);

        KeyDown += OnKeyDown;
    }

    public KaraokeDisplayControl Display => _display;

    public void UpdateTime(double positionSeconds)
    {
        var ts = TimeSpan.FromSeconds(positionSeconds);
        _lblTime.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
        _display.SetPosition(positionSeconds);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Space)
        {
            _togglePlayback();
            e.Handled = true;
        }
    }
}
