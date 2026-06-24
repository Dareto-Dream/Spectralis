using System.Drawing;

namespace Spectralis;

internal sealed class BeatGridOverlayControl : Control
{
    private float    _bpm;
    private TimeSpan _firstBeatOffset;
    private double   _currentPosition;
    private double   _duration;

    public BeatGridOverlayControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        Height    = 6;
    }

    public void SetGrid(float bpm, TimeSpan firstBeatOffset)
    {
        _bpm             = bpm;
        _firstBeatOffset = firstBeatOffset;
        Invalidate();
    }

    public void SetPosition(double currentPositionSeconds, double durationSeconds)
    {
        _currentPosition = currentPositionSeconds;
        _duration        = durationSeconds;
        Invalidate();
    }

    public Color TickColor { get; set; } = Color.FromArgb(160, 255, 200, 80);

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (_bpm <= 0 || _duration <= 0) return;

        var g = e.Graphics;
        var beatPeriod = 60.0 / _bpm;
        var firstBeat  = _firstBeatOffset.TotalSeconds;

        using var pen = new Pen(TickColor, 1f);
        var w = Width;
        var h = Height;

        // Draw ticks from firstBeat onwards, up to duration
        var t = firstBeat;
        while (t <= _duration)
        {
            var x = (int)(t / _duration * w);
            g.DrawLine(pen, x, 0, x, h - 1);
            t += beatPeriod;
        }
    }
}
