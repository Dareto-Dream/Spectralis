using System.Numerics;

namespace Spectralis.Core.Visualizers.Scripting;

/// <summary>
/// JS canvas-compatible API exposed to scripted visualizers via Jint.
/// Wraps IVizCanvas so scripts run on any rendering backend.
/// </summary>
public sealed class ScriptCanvasContext
{
    private IVizCanvas? _canvas;
    private VizRect _bounds;
    private VizColor _fillColor = new(255, 255, 255, 255);
    private VizColor _strokeColor = new(255, 255, 255, 255);
    private float _lineWidth = 1f;
    private float _fontSize = 12f;
    private readonly List<Vector2> _pathPoints = new();

    internal void Begin(IVizCanvas canvas, VizRect bounds)
    {
        _canvas = canvas;
        _bounds = bounds;
        _fillColor = new VizColor(255, 255, 255, 255);
        _strokeColor = new VizColor(255, 255, 255, 255);
        _lineWidth = 1f;
        _fontSize = 12f;
        _pathPoints.Clear();
    }

    public void setFill(string color) => _fillColor = ParseColor(color);

    public void setStroke(string color) => _strokeColor = ParseColor(color);

    public void setLineWidth(double w) => _lineWidth = (float)Math.Max(0.1, w);

    public void setFont(double size, string? family = null) =>
        _fontSize = (float)Math.Max(6, size);

    public void fillRect(double x, double y, double w, double h)
    {
        if (_canvas is null || w <= 0 || h <= 0) return;
        _canvas.FillRect(new VizRect((float)x, (float)y, (float)w, (float)h), _fillColor);
    }

    public void strokeRect(double x, double y, double w, double h)
    {
        if (_canvas is null) return;
        _canvas.DrawRoundedRect(
            new VizRect((float)x, (float)y, (float)w, (float)h),
            0f, _strokeColor, _lineWidth);
    }

    public void fillText(string s, double x, double y)
    {
        if (_canvas is null || string.IsNullOrEmpty(s)) return;
        _canvas.DrawText(s,
            new VizRect((float)x, (float)y, _bounds.Width, _fontSize + 4),
            _fillColor, _fontSize, VizTextAlign.Left);
    }

    public void beginPath() => _pathPoints.Clear();

    public void moveTo(double x, double y) => _pathPoints.Add(new Vector2((float)x, (float)y));

    public void lineTo(double x, double y) => _pathPoints.Add(new Vector2((float)x, (float)y));

    public void arc(double cx, double cy, double r, double startAngle, double endAngle)
    {
        const int steps = 20;
        double span = endAngle - startAngle;
        for (int i = 0; i <= steps; i++)
        {
            double a = startAngle + span * i / steps;
            _pathPoints.Add(new Vector2(
                (float)(cx + r * Math.Cos(a)),
                (float)(cy + r * Math.Sin(a))));
        }
    }

    public void closePath()
    {
        if (_pathPoints.Count > 0)
            _pathPoints.Add(_pathPoints[0]);
    }

    public void stroke()
    {
        if (_canvas is null || _pathPoints.Count < 2) return;
        _canvas.DrawPolyline(_pathPoints.ToArray().AsSpan(), _strokeColor, _lineWidth);
    }

    public void fill()
    {
        if (_canvas is null || _pathPoints.Count < 3) return;
        _canvas.FillPolygon(_pathPoints.ToArray().AsSpan(), _fillColor);
    }

    public void clearRect(double x, double y, double w, double h)
    {
        if (_canvas is null) return;
        _canvas.FillRect(new VizRect((float)x, (float)y, (float)w, (float)h), new VizColor(255, 0, 0, 0));
    }

    private static VizColor ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return new VizColor(255, 255, 255, 255);
        s = s.Trim();
        try
        {
            if (s.StartsWith('#'))
            {
                s = s[1..];
                if (s.Length == 3)
                    s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
                if (s.Length == 6)
                {
                    byte r = Convert.ToByte(s[..2], 16);
                    byte g = Convert.ToByte(s[2..4], 16);
                    byte b = Convert.ToByte(s[4..6], 16);
                    return new VizColor(255, r, g, b);
                }
                if (s.Length == 8)
                {
                    byte a = Convert.ToByte(s[..2], 16);
                    byte r = Convert.ToByte(s[2..4], 16);
                    byte g = Convert.ToByte(s[4..6], 16);
                    byte b = Convert.ToByte(s[6..8], 16);
                    return new VizColor(a, r, g, b);
                }
            }
            if (s.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase))
            {
                var parts = s[4..^1].Split(',');
                if (parts.Length == 3)
                    return new VizColor(255,
                        byte.Parse(parts[0].Trim()), byte.Parse(parts[1].Trim()), byte.Parse(parts[2].Trim()));
            }
            if (s.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
            {
                var parts = s[5..^1].Split(',');
                if (parts.Length == 4)
                    return new VizColor(
                        (byte)(float.Parse(parts[3].Trim()) * 255),
                        byte.Parse(parts[0].Trim()), byte.Parse(parts[1].Trim()), byte.Parse(parts[2].Trim()));
            }
            // Named colors
            return s.ToLowerInvariant() switch
            {
                "black"   => new VizColor(255, 0, 0, 0),
                "white"   => new VizColor(255, 255, 255, 255),
                "red"     => new VizColor(255, 255, 0, 0),
                "green"   => new VizColor(255, 0, 128, 0),
                "blue"    => new VizColor(255, 0, 0, 255),
                "yellow"  => new VizColor(255, 255, 255, 0),
                "cyan"    => new VizColor(255, 0, 255, 255),
                "magenta" => new VizColor(255, 255, 0, 255),
                "orange"  => new VizColor(255, 255, 165, 0),
                "purple"  => new VizColor(255, 128, 0, 128),
                "gray"    => new VizColor(255, 128, 128, 128),
                "grey"    => new VizColor(255, 128, 128, 128),
                _         => new VizColor(255, 255, 255, 255),
            };
        }
        catch
        {
            return new VizColor(255, 255, 255, 255);
        }
    }
}
