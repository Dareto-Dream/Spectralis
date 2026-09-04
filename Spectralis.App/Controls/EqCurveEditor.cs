using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.Controls;

/// <summary>
/// Interactive frequency-response ("envelope") editor for the parametric EQ.
/// Draws the combined magnitude response as a filled curve and one draggable
/// handle per band: drag = frequency + gain, Ctrl+wheel = Q, double-click empty
/// space = add band, right-click a handle = filter type / enable / delete.
/// </summary>
public sealed class EqCurveEditor : Control
{
    private const double MinHz = 20;
    private const double MaxHz = 20000;
    private const int CurveResolution = 220;
    private const double HandleRadius = 5.5;
    private const double HitRadius = 12;

    public static readonly StyledProperty<EqEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<EqCurveEditor, EqEditorViewModel?>(nameof(Editor));

    public static readonly StyledProperty<IBrush?> CurveBrushProperty =
        AvaloniaProperty.Register<EqCurveEditor, IBrush?>(nameof(CurveBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<EqCurveEditor, IBrush?>(nameof(GridBrush));

    public static readonly StyledProperty<IBrush?> TextBrushProperty =
        AvaloniaProperty.Register<EqCurveEditor, IBrush?>(nameof(TextBrush));

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);

    private EqEditorViewModel? _hooked;
    private int _dragBandIndex = -1;
    private int _hoverBandIndex = -1;
    private Rect _plot;
    private double _maxDb = 15;
    private readonly DispatcherTimer _dragRedraw;

    public EqCurveEditor()
    {
        Focusable = true;
        _dragRedraw = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _dragRedraw.Tick += (_, _) => InvalidateVisual();
    }

    static EqCurveEditor()
    {
        AffectsRender<EqCurveEditor>(CurveBrushProperty, GridBrushProperty, TextBrushProperty);
    }

    public EqEditorViewModel? Editor
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

            _hooked = change.GetNewValue<EqEditorViewModel?>();
            if (_hooked is not null)
            {
                _hooked.CurveChanged += OnEditorChanged;
            }

            InvalidateVisual();
        }
    }

    private void OnEditorChanged(object? sender, EventArgs e) => InvalidateVisual();

    // ---- geometry helpers -------------------------------------------------

    private double FreqToX(double hz) =>
        _plot.X + (Math.Log10(hz / MinHz) / Math.Log10(MaxHz / MinHz) * _plot.Width);

    private double XToFreq(double x) =>
        MinHz * Math.Pow(MaxHz / MinHz, (x - _plot.X) / _plot.Width);

    private double GainToY(double db) =>
        _plot.Y + (_plot.Height / 2) - (db / _maxDb * (_plot.Height / 2));

    private double YToGain(double y) =>
        (_plot.Y + (_plot.Height / 2) - y) / (_plot.Height / 2) * _maxDb;

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

        const double leftPad = 30;
        const double bottomPad = 16;
        const double pad = 6;
        _plot = new Rect(leftPad, pad, Math.Max(1, w - leftPad - pad), Math.Max(1, h - pad - bottomPad));

        var grid = GridBrush ?? new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        var text = TextBrush ?? new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
        var accent = (CurveBrush as ISolidColorBrush)?.Color ?? Color.FromRgb(240, 176, 64);
        var gridPen = new Pen(grid, 1);
        var zeroPen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);

        var curve = editor.ComputeResponseCurve(CurveResolution, MinHz, MaxHz);
        var peak = 1.0;
        foreach (var d in curve)
        {
            peak = Math.Max(peak, Math.Abs(d));
        }

        _maxDb = Math.Clamp(Math.Ceiling(peak / 3) * 3, 12, 24);

        // dB gridlines + labels
        for (var db = -_maxDb; db <= _maxDb + 0.01; db += 6)
        {
            var y = GainToY(db);
            context.DrawLine(Math.Abs(db) < 0.01 ? zeroPen : gridPen, new Point(_plot.X, y), new Point(_plot.Right, y));
            var label = new FormattedText(
                $"{(db > 0 ? "+" : string.Empty)}{db:0}",
                System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                9,
                text);
            context.DrawText(label, new Point(2, y - (label.Height / 2)));
        }

        // frequency gridlines + labels
        double[] freqLines = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000];
        foreach (var f in freqLines)
        {
            var x = FreqToX(f);
            context.DrawLine(gridPen, new Point(x, _plot.Y), new Point(x, _plot.Bottom));
            if (f is 100 or 1000 or 10000)
            {
                var lbl = new FormattedText(
                    f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}",
                    System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Typeface.Default,
                    9,
                    text);
                context.DrawText(lbl, new Point(x - (lbl.Width / 2), _plot.Bottom + 3));
            }
        }

        // combined response curve, filled down to the 0 dB line
        var fill = new StreamGeometry();
        var stroke = new StreamGeometry();
        using (var fc = fill.Open())
        using (var sc = stroke.Open())
        {
            var zeroY = GainToY(0);
            for (var i = 0; i < curve.Length; i++)
            {
                var x = _plot.X + (_plot.Width * i / (curve.Length - 1));
                var y = Math.Clamp(GainToY(curve[i]), _plot.Y, _plot.Bottom);
                if (i == 0)
                {
                    fc.BeginFigure(new Point(x, zeroY), true);
                    fc.LineTo(new Point(x, y));
                    sc.BeginFigure(new Point(x, y), false);
                }
                else
                {
                    fc.LineTo(new Point(x, y));
                    sc.LineTo(new Point(x, y));
                }
            }

            fc.LineTo(new Point(_plot.Right, zeroY));
            fc.EndFigure(true);
            sc.EndFigure(false);
        }

        context.DrawGeometry(new SolidColorBrush(accent, 0.16), null, fill);
        context.DrawGeometry(null, new Pen(new SolidColorBrush(accent), 2), stroke);

        // band handles
        var bands = editor.Bands;
        for (var i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            var x = FreqToX(Math.Clamp(b.Frequency, MinHz, MaxHz));
            var y = b.GainMatters ? Math.Clamp(GainToY(b.GainDb), _plot.Y, _plot.Bottom) : GainToY(0);
            var r = (i == _dragBandIndex || i == _hoverBandIndex) ? HandleRadius + 2 : HandleRadius;
            var handleFill = b.Enabled ? new SolidColorBrush(accent) : null;
            context.DrawEllipse(handleFill, new Pen(new SolidColorBrush(accent), 1.5), new Point(x, y), r, r);

            if (i == _dragBandIndex || i == _hoverBandIndex)
            {
                var info = new FormattedText(
                    $"{b.Label}  {(b.GainMatters ? $"{b.GainDb:+0.0;-0.0;0} dB  " : string.Empty)}Q{b.Q:0.0}  {b.Type}",
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
        var bands = Editor?.Bands;
        if (bands is null)
        {
            return -1;
        }

        var best = -1;
        var bestDist = HitRadius;
        for (var i = 0; i < bands.Count; i++)
        {
            var b = bands[i];
            var x = FreqToX(Math.Clamp(b.Frequency, MinHz, MaxHz));
            var y = b.GainMatters ? GainToY(b.GainDb) : GainToY(0);
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
        var props = e.GetCurrentPoint(this).Properties;
        var hit = HitTest(p);

        if (props.IsRightButtonPressed)
        {
            if (hit >= 0)
            {
                ShowBandMenu(editor, hit);
            }

            return;
        }

        if (hit >= 0)
        {
            if (e.ClickCount == 2)
            {
                editor.RemoveBand(editor.Bands[hit]);
                _hoverBandIndex = -1;
                InvalidateVisual();
                return;
            }

            _dragBandIndex = hit;
            e.Pointer.Capture(this);
            _dragRedraw.Start();
            e.Handled = true;
        }
        else if (e.ClickCount == 2 && _plot.Contains(p))
        {
            editor.AddBandAt(XToFreq(p.X), YToGain(p.Y));
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

        if (_dragBandIndex >= 0 && _dragBandIndex < editor.Bands.Count)
        {
            var band = editor.Bands[_dragBandIndex];
            band.Frequency = Math.Clamp(XToFreq(Math.Clamp(p.X, _plot.X, _plot.Right)), MinHz, MaxHz);
            if (band.GainMatters)
            {
                band.GainDb = Math.Clamp(YToGain(p.Y), -_maxDb, _maxDb);
            }

            return;
        }

        var hover = HitTest(p);
        if (hover != _hoverBandIndex)
        {
            _hoverBandIndex = hover;
            Cursor = hover >= 0 ? HandCursor : ArrowCursor;
            InvalidateVisual();
        }
    }

    protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _dragRedraw.Stop();
        _dragBandIndex = -1;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragBandIndex >= 0)
        {
            _dragBandIndex = -1;
            _dragRedraw.Stop();
            e.Pointer.Capture(null);
            InvalidateVisual();
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var editor = Editor;
        if (editor is null)
        {
            return;
        }

        var idx = _dragBandIndex >= 0 ? _dragBandIndex : HitTest(e.GetPosition(this));
        if (idx < 0 || idx >= editor.Bands.Count)
        {
            return;
        }

        var band = editor.Bands[idx];
        var factor = Math.Pow(1.2, e.Delta.Y > 0 ? 1 : -1);
        band.Q = Math.Clamp(band.Q * factor, 0.1, 18);
        e.Handled = true;
        InvalidateVisual();
    }

    private void ShowBandMenu(EqEditorViewModel editor, int index)
    {
        var band = editor.Bands[index];
        var menu = new MenuFlyout();

        foreach (var type in band.FilterTypes)
        {
            var item = new MenuItem
            {
                Header = type.ToString(),
                Icon = type == band.Type ? new TextBlock { Text = "✓" } : null,
            };
            var captured = type;
            item.Click += (_, _) => band.Type = captured;
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var toggle = new MenuItem { Header = band.Enabled ? "Disable band" : "Enable band" };
        toggle.Click += (_, _) => band.Enabled = !band.Enabled;
        menu.Items.Add(toggle);

        var remove = new MenuItem { Header = "Delete band", IsEnabled = editor.CanRemoveBand };
        remove.Click += (_, _) => editor.RemoveBand(band);
        menu.Items.Add(remove);

        menu.ShowAt(this, showAtPointer: true);
    }
}
