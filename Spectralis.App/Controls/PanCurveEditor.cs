using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Controls;

/// <summary>
/// Interactive auto-pan loop editor: draws the pan curve over one loop (bar-gridded
/// by <see cref="PanEditorViewModel.Bars"/>) and one draggable handle per node.
/// Drag = time + pan, double-click empty space = add a node, double-click a handle
/// = remove it. Mirrors <see cref="EqCurveEditor"/>'s interaction model with a
/// linear time axis instead of log-frequency.
/// </summary>
public sealed class PanCurveEditor : Control
{
    private const int CurveResolution = 220;
    private const double HandleRadius = 5.5;
    private const double HitRadius = 12;

    public static readonly StyledProperty<PanEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<PanCurveEditor, PanEditorViewModel?>(nameof(Editor));

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<PanCurveEditor, IBrush?>(nameof(CurveBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<PanCurveEditor, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<PanCurveEditor, IBrush?>(nameof(TextBrush));

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private PanEditorViewModel? _hooked;
    private int _dragPointIndex = -1;
    private int _hoverPointIndex = -1;
    private Rect _plot;
    private readonly DispatcherTimer _dragRedraw;

    public PanCurveEditor()
    {
        Focusable = true;
        _dragRedraw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _dragRedraw.Tick += (_, _) => InvalidateVisual();
    }

    static PanCurveEditor()
    {
        AffectsRender<PanCurveEditor>(CurveBrushProperty, GridBrushProperty, TextBrushProperty);
    }

    public PanEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    public IBrush? CurveBrush
    {
        get => GetValue(CurveBrushProperty);
        set => SetValue(CurveBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public IBrush? TextBrush
    {
        get => GetValue(TextBrushProperty);
        set => SetValue(TextBrushProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == EditorProperty)
        {
            if (_hooked is not null)
            {
                _hooked.CurveChanged -= OnEditorChanged;
            }

            _hooked = change.GetNewValue<PanEditorViewModel?>();
            if (_hooked is not null)
            {
                _hooked.CurveChanged += OnEditorChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnEditorChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---- geometry helpers -------------------------------------------------

    private double TimeToX(double time) => _plot.X + (time * _plot.Width);

    private double XToTime(double x) => (x - _plot.X) / _plot.Width;

    private double PanToY(double pan) => _plot.Y + (_plot.Height / 2) - (pan * (_plot.Height / 2));

    private double YToPan(double y) => (_plot.Y + (_plot.Height / 2) - y) / (_plot.Height / 2);

    // ---- rendering ------------------------------------------------------

    public override void Render(DrawingContext context)
    {
        var editor = Editor;
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (editor is null || w < 40 || h < 40)
        {
            return;
        }

        const double leftPad = 26;
        const double bottomPad = 16;
        const double pad = 6;
        _plot = new Rect(leftPad, pad, Math.Max(1, w - leftPad - pad), Math.Max(1, h - pad - bottomPad));

        var grid = GridBrush ?? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        var text = TextBrush ?? new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        var accent = (CurveBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(240, 176, 64);
        var gridPen = new Pen(grid, 1);
        var zeroPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);

        // Pan gridlines: L / center / R.
        foreach (var pan in new[] { -1.0, 0.0, 1.0 })
        {
            var y = PanToY(pan);
            context.DrawLine(Math.Abs(pan) < 0.01 ? zeroPen : gridPen, new Point(_plot.X, y), new Point(_plot.Right, y));
            var label = new FormattedText(
                pan < 0 ? "L" : pan > 0 ? "R" : "C",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                text);
            context.DrawText(label, new Point(2, y - (label.Height / 2)));
        }

        // Bar gridlines, quarter-beat ticks within each bar.
        var bars = Math.Max(1, editor.Bars);
        for (var bar = 0; bar <= bars; bar++)
        {
            var x = TimeToX((double)bar / bars);
            context.DrawLine(gridPen, new Point(x, _plot.Y), new Point(x, _plot.Bottom));
            if (bar < bars)
            {
                var label = new FormattedText(
                    $"{bar + 1}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    9,
                    text);
                context.DrawText(label, new Point(x + 3, _plot.Bottom + 3));

                for (var beat = 1; beat < 4; beat++)
                {
                    var bx = TimeToX(((bar * 4) + beat) / (bars * 4.0));
                    var beatPen = new Pen(grid, 1, dashStyle: DashStyle.Dot);
                    context.DrawLine(beatPen, new Point(bx, _plot.Y), new Point(bx, _plot.Bottom));
                }
            }
        }

        // Pan curve, filled to the center line.
        var curve = editor.ComputeCurve(CurveResolution);
        var fill = new StreamGeometry();
        var stroke = new StreamGeometry();
        using (var fc = fill.Open())
        using (var sc = stroke.Open())
        {
            var centerY = PanToY(0);
            for (var i = 0; i < curve.Length; i++)
            {
                var x = _plot.X + (_plot.Width * i / (curve.Length - 1));
                var y = Math.Clamp(PanToY(curve[i]), _plot.Y, _plot.Bottom);
                if (i == 0)
                {
                    fc.BeginFigure(new Point(x, centerY), true);
                    fc.LineTo(new Point(x, y));
                    sc.BeginFigure(new Point(x, y), false);
                }
                else
                {
                    fc.LineTo(new Point(x, y));
                    sc.LineTo(new Point(x, y));
                }
            }

            fc.LineTo(new Point(_plot.Right, centerY));
            fc.EndFigure(true);
            sc.EndFigure(false);
        }

        context.DrawGeometry(new SolidColorBrush(accent, 0.16), null, fill);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2), stroke);

        // Node handles.
        var points = editor.Points;
        for (var i = 0; i < points.Count; i++)
        {
            var p = points[i];
            var x = TimeToX(Math.Clamp(p.Time, 0, 1));
            var y = Math.Clamp(PanToY(Math.Clamp(p.Pan, -1, 1)), _plot.Y, _plot.Bottom);
            var r = (i == _dragPointIndex || i == _hoverPointIndex) ? HandleRadius + 2 : HandleRadius;
            context.DrawEllipse(new SolidColorBrush(accent), new Pen(new SolidColorBrush(accent), 1.5), new Point(x, y), r, r);

            if (i == _dragPointIndex || i == _hoverPointIndex)
            {
                var info = new FormattedText(
                    $"{p.Time * bars:0.00} bar  {(p.Pan > 0 ? "R" : p.Pan < 0 ? "L" : "C")} {Math.Abs(p.Pan):0.00}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    9,
                    text);
                var tx = Math.Clamp(x - (info.Width / 2), _plot.X, _plot.Right - info.Width);
                context.DrawText(info, new Point(tx, _plot.Y + 1));
            }
        }
    }

    // ---- interaction ---------------------------------------------------

    private int HitTest(Point p)
    {
        var points = Editor?.Points;
        if (points is null)
        {
            return -1;
        }

        var best = -1;
        var bestDist = HitRadius;
        for (var i = 0; i < points.Count; i++)
        {
            var x = TimeToX(points[i].Time);
            var y = PanToY(points[i].Pan);
            var d = Math.Sqrt(Math.Pow(p.X - x, 2) + Math.Pow(p.Y - y, 2));
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
            }
        }

        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var editor = Editor;
        if (editor is null)
        {
            return;
        }

        var p = e.GetPosition(this);
        var hit = HitTest(p);

        if (hit >= 0)
        {
            if (e.ClickCount == 2)
            {
                editor.RemovePoint(editor.Points[hit]);
                _hoverPointIndex = -1;
                InvalidateVisual();
                return;
            }

            _dragPointIndex = hit;
            e.Pointer.Capture(this);
            _dragRedraw.Start();
            e.Handled = true;
        }
        else if (e.ClickCount == 2 && _plot.Contains(p))
        {
            editor.AddPointAt(XToTime(p.X), YToPan(p.Y));
            InvalidateVisual();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var editor = Editor;
        if (editor is null)
        {
            return;
        }

        var p = e.GetPosition(this);

        if (_dragPointIndex >= 0 && _dragPointIndex < editor.Points.Count)
        {
            var point = editor.Points[_dragPointIndex];
            point.Time = Math.Clamp(XToTime(Math.Clamp(p.X, _plot.X, _plot.Right)), 0, 1);
            point.Pan = Math.Clamp(YToPan(Math.Clamp(p.Y, _plot.Y, _plot.Bottom)), -1, 1);
            return;
        }

        var hover = HitTest(p);
        if (hover != _hoverPointIndex)
        {
            _hoverPointIndex = hover;
            Cursor = hover >= 0 ? HandCursor : ArrowCursor;
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _dragRedraw.Stop();
        _dragPointIndex = -1;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragPointIndex >= 0)
        {
            _dragPointIndex = -1;
            _dragRedraw.Stop();
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }
}
