using System.Drawing;

namespace Spectralis;

internal sealed class ScriptCanvasContext
{
    private Graphics? _g;
    private Color _fillColor = Color.White;
    private Color _strokeColor = Color.White;
    private float _lineWidth = 1f;
    private string _fontFamily = "Segoe UI";
    private float _fontSize = 12f;
    private readonly List<PointF> _pathPoints = new();

    internal void Begin(Graphics g)
    {
        _g = g;
        _fillColor = Color.White;
        _strokeColor = Color.White;
        _lineWidth = 1f;
        _fontFamily = "Segoe UI";
        _fontSize = 12f;
        _pathPoints.Clear();
    }

    public void setFill(string color) => _fillColor = ParseColor(color);

    public void setStroke(string color) => _strokeColor = ParseColor(color);

    public void setLineWidth(double w) => _lineWidth = (float)Math.Max(0.1, w);

    public void setFont(double size, string? family = null)
    {
        _fontSize = (float)Math.Max(6, size);
        if (!string.IsNullOrWhiteSpace(family)) _fontFamily = family;
    }

    public void fillRect(double x, double y, double w, double h)
    {
        if (_g is null || w <= 0 || h <= 0) return;
        using var b = new SolidBrush(_fillColor);
        _g.FillRectangle(b, (float)x, (float)y, (float)w, (float)h);
    }

    public void strokeRect(double x, double y, double w, double h)
    {
        if (_g is null) return;
        using var p = new Pen(_strokeColor, _lineWidth);
        _g.DrawRectangle(p, (float)x, (float)y, (float)w, (float)h);
    }

    public void fillText(string s, double x, double y)
    {
        if (_g is null || string.IsNullOrEmpty(s)) return;
        using var font = new Font(_fontFamily, _fontSize, GraphicsUnit.Pixel);
        using var b = new SolidBrush(_fillColor);
        _g.DrawString(s, font, b, (float)x, (float)y);
    }

    public void beginPath() => _pathPoints.Clear();

    public void moveTo(double x, double y) => _pathPoints.Add(new PointF((float)x, (float)y));

    public void lineTo(double x, double y) => _pathPoints.Add(new PointF((float)x, (float)y));

    public void arc(double cx, double cy, double r, double startAngle, double endAngle)
    {
        int steps = 20;
        double span = endAngle - startAngle;
        for (int i = 0; i <= steps; i++)
        {
            double a = startAngle + span * i / steps;
            _pathPoints.Add(new PointF((float)(cx + r * Math.Cos(a)), (float)(cy + r * Math.Sin(a))));
        }
    }

    public void closePath()
    {
        if (_pathPoints.Count > 0)
            _pathPoints.Add(_pathPoints[0]);
    }

    public void stroke()
    {
        if (_g is null || _pathPoints.Count < 2) return;
        using var p = new Pen(_strokeColor, _lineWidth);
        _g.DrawLines(p, _pathPoints.ToArray());
    }

    public void fill()
    {
        if (_g is null || _pathPoints.Count < 3) return;
        using var b = new SolidBrush(_fillColor);
        _g.FillPolygon(b, _pathPoints.ToArray());
    }

    public void clearRect(double x, double y, double w, double h)
    {
        if (_g is null) return;
        var state = _g.Save();
        _g.SetClip(new RectangleF((float)x, (float)y, (float)w, (float)h));
        _g.Clear(Color.Black);
        _g.Restore(state);
    }

    private static Color ParseColor(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return Color.White;
        try { return ColorTranslator.FromHtml(s); }
        catch { return Color.White; }
    }
}
