using System.Drawing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Spectralis;

internal sealed class MetronomeForm : Form
{
    private readonly ThemePalette _theme;

    private System.Threading.Timer? _timer;
    private WaveOutEvent?           _waveOut;
    private float                   _bpm = 120f;
    private bool                    _running;
    private DateTime                _lastFlash;

    // ── Controls ──────────────────────────────────────────────────────────────
    private readonly Panel  flashPanel  = new();
    private readonly Label  lblBpm      = new();
    private readonly NumericUpDown nudBpm = new();
    private readonly Button btnToggle   = new();
    private readonly CheckBox chkAudio  = new();
    private readonly Label  lblTap      = new();
    private DateTime _lastTapTime = DateTime.MinValue;
    private readonly List<double> _tapIntervals = [];

    public MetronomeForm(ThemePalette theme, float initialBpm = 120f)
    {
        _theme         = theme;
        _bpm           = Math.Clamp(initialBpm, 40f, 240f);
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Metronome";
        FormBorderStyle     = FormBorderStyle.FixedToolWindow;
        StartPosition       = FormStartPosition.CenterParent;
        TopMost             = true;
        ClientSize          = new Size(240, 220);
        ShowInTaskbar       = false;

        BuildLayout();
        ApplyTheme();
        nudBpm.Value = (decimal)_bpm;
    }

    private void BuildLayout()
    {
        SuspendLayout();

        flashPanel.Dock      = DockStyle.Top;
        flashPanel.Height    = 56;
        flashPanel.BackColor = _theme.SurfaceAltBackColor;

        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 2,
            RowCount    = 3,
            Padding     = new Padding(12, 8, 12, 12),
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        lblBpm.Text      = "BPM";
        lblBpm.AutoSize  = false;
        lblBpm.Dock      = DockStyle.Fill;
        lblBpm.TextAlign = ContentAlignment.MiddleLeft;
        lblBpm.Font      = new Font("Segoe UI", 9f);

        nudBpm.Minimum       = 40;
        nudBpm.Maximum       = 240;
        nudBpm.DecimalPlaces = 1;
        nudBpm.Increment     = (decimal)0.5;
        nudBpm.Dock          = DockStyle.Fill;
        nudBpm.Font          = new Font("Segoe UI", 9f);
        nudBpm.ValueChanged += (_, _) =>
        {
            _bpm = (float)nudBpm.Value;
            if (_running) RestartTimer();
        };

        btnToggle.Text      = "▶ Start";
        btnToggle.FlatStyle = FlatStyle.Flat;
        btnToggle.Dock      = DockStyle.Fill;
        btnToggle.Margin    = new Padding(0, 2, 6, 2);
        btnToggle.Click    += BtnToggle_Click;

        chkAudio.Text      = "Audio click";
        chkAudio.Checked   = true;
        chkAudio.AutoSize  = false;
        chkAudio.Dock      = DockStyle.Fill;
        chkAudio.TextAlign = ContentAlignment.MiddleLeft;

        lblTap.Text        = "Tap for BPM";
        lblTap.AutoSize    = false;
        lblTap.Dock        = DockStyle.Fill;
        lblTap.Font        = new Font("Segoe UI", 9f);
        lblTap.ForeColor   = _theme.TextMutedColor;
        lblTap.TextAlign   = ContentAlignment.MiddleCenter;
        lblTap.Cursor      = Cursors.Hand;
        lblTap.Click      += LblTap_Click;
        lblTap.BorderStyle = BorderStyle.FixedSingle;

        content.Controls.Add(lblBpm, 0, 0);
        content.Controls.Add(nudBpm, 1, 0);
        content.Controls.Add(btnToggle, 0, 1);
        content.Controls.Add(chkAudio, 1, 1);
        content.Controls.Add(lblTap, 0, 2);
        content.SetColumnSpan(lblTap, 2);

        Controls.AddRange([flashPanel, content]);
        ResumeLayout(false);
    }

    private void ApplyTheme()
    {
        BackColor  = _theme.WindowBackColor;
        ForeColor  = _theme.TextPrimaryColor;

        lblBpm.ForeColor  = _theme.TextSecondaryColor;
        nudBpm.BackColor  = _theme.SurfaceAltBackColor;
        nudBpm.ForeColor  = _theme.TextPrimaryColor;

        btnToggle.BackColor = _theme.SurfaceRaisedColor;
        btnToggle.ForeColor = _theme.TextSecondaryColor;
        btnToggle.FlatAppearance.BorderColor = _theme.BorderStrongColor;

        chkAudio.ForeColor = _theme.TextSecondaryColor;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        Stop();
        base.OnFormClosing(e);
    }

    // ── Metronome control ─────────────────────────────────────────────────────

    private void BtnToggle_Click(object? sender, EventArgs e)
    {
        if (_running) Stop();
        else          Start();
    }

    private void Start()
    {
        _running        = true;
        btnToggle.Text  = "■ Stop";
        InitAudio();
        RestartTimer();
    }

    private void Stop()
    {
        _running        = false;
        btnToggle.Text  = "▶ Start";
        _timer?.Dispose();
        _timer = null;
        _waveOut?.Dispose();
        _waveOut = null;
    }

    private void RestartTimer()
    {
        _timer?.Dispose();
        var interval = (int)Math.Round(60_000.0 / _bpm);
        _timer = new System.Threading.Timer(OnTick, null, 0, interval);
    }

    private void OnTick(object? _)
    {
        if (chkAudio.Checked && _waveOut is not null)
            PlayClick();

        if (!IsHandleCreated || IsDisposed) return;
        try
        {
            BeginInvoke(new Action(FlashBeat));
        }
        catch { }
    }

    private void FlashBeat()
    {
        _lastFlash      = DateTime.UtcNow;
        flashPanel.BackColor = _theme.AccentPrimaryColor;

        var flashTimer = new System.Windows.Forms.Timer { Interval = 80 };
        flashTimer.Tick += (_, _) =>
        {
            flashPanel.BackColor = _theme.SurfaceAltBackColor;
            flashTimer.Stop();
            flashTimer.Dispose();
        };
        flashTimer.Start();
    }

    // ── Tap tempo ─────────────────────────────────────────────────────────────

    private void LblTap_Click(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_lastTapTime != DateTime.MinValue)
        {
            var interval = (now - _lastTapTime).TotalMilliseconds;
            if (interval < 3000)
            {
                _tapIntervals.Add(interval);
                if (_tapIntervals.Count > 8) _tapIntervals.RemoveAt(0);

                var avgInterval = _tapIntervals.Average();
                _bpm = (float)Math.Round(60_000.0 / avgInterval, 1);
                _bpm = Math.Clamp(_bpm, 40f, 240f);
                nudBpm.Value = (decimal)_bpm;
            }
            else
            {
                _tapIntervals.Clear();
            }
        }
        _lastTapTime = now;
    }

    // ── Audio click ───────────────────────────────────────────────────────────

    private void InitAudio()
    {
        try
        {
            _waveOut?.Dispose();
            _waveOut = new WaveOutEvent { DesiredLatency = 100 };
        }
        catch { _waveOut = null; }
    }

    private void PlayClick()
    {
        if (_waveOut is null) return;
        try
        {
            // 30ms 880Hz sine with exponential decay
            const int sr    = 44100;
            const int count = sr / 33;  // ~30ms
            var samples = new float[count * 2];  // stereo
            for (var i = 0; i < count; i++)
            {
                var s = (float)(0.7 * Math.Sin(2 * Math.PI * 880.0 * i / sr)
                                     * Math.Exp(-i / (sr / 100.0)));
                samples[i * 2]     = s;
                samples[i * 2 + 1] = s;
            }
            var provider = new RawSourceWaveStream(
                new System.IO.MemoryStream(FloatsToBytes(samples)),
                WaveFormat.CreateIeeeFloatWaveFormat(sr, 2));
            var clicked = new WaveOutEvent { DesiredLatency = 100 };
            clicked.Init(provider);
            clicked.Play();
            // Dispose after playing
            clicked.PlaybackStopped += (_, _) => { clicked.Dispose(); provider.Dispose(); };
        }
        catch { }
    }

    private static byte[] FloatsToBytes(float[] samples)
    {
        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
