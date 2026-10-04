using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Spectralis.App.ViewModels;
using Spectralis.Core.Formats;

namespace Spectralis.App.Controls;

/// <summary>
/// Draws the timeline editor (ruler, section band, one lane per event target, playhead) and turns pointer
/// input into edits on <see cref="TimelineEditorViewModel"/>. All geometry and hit-testing come from
/// <see cref="TimelineLayout"/>; this class is the drawing and gesture shell around it.
///
/// Click a block to select, drag to move, drag a long event's right edge or a section's edge to resize,
/// double-click empty lane space to add an event, click the ruler to seek. Each drag is one undo step.
/// </summary>
public sealed class TimelineCanvas : Control
{
    public static readonly StyledProperty<TimelineEditorViewModel?> EditorProperty =
        AvaloniaProperty.Register<TimelineCanvas, TimelineEditorViewModel?>(nameof(Editor));

    private static readonly Typeface Face = new("Segoe UI, Inter, sans-serif");
    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private static readonly Color Signal = Color.Parse("#FF4E1A");
    private static readonly Color Ink = Color.Parse("#E9EBEE");
    private static readonly Color InkMuted = Color.Parse("#5A616C");
    private static readonly Color Raised = Color.Parse("#15171B");
    private static readonly Color Overlay = Color.Parse("#1D2026");
    private static readonly Color Border = Color.Parse("#23262C");

    private enum Drag { None, Event, EventEnd, Section, SectionStart, SectionEnd, Seek }

    private TimelineEditorViewModel? _hooked;
    private Drag _drag = Drag.None;
    private double _grabOffset; // seconds between the pointer and the dragged block's start

    static TimelineCanvas()
    {
        AffectsMeasure<TimelineCanvas>(EditorProperty);
        AffectsRender<TimelineCanvas>(EditorProperty);
    }

    public TimelineCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public TimelineEditorViewModel? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != EditorProperty) return;

        if (_hooked is not null) _hooked.Invalidated -= OnInvalidated;
        _hooked = change.GetNewValue<TimelineEditorViewModel?>();
        if (_hooked is not null) _hooked.Invalidated += OnInvalidated;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnInvalidated()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InvalidateMeasure(); // content size follows zoom, track length and lane count
            InvalidateVisual();
        });
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var vm = Editor;
        if (vm is null) return new Size(0, 0);
        return new Size(vm.ContentWidth, Math.Max(vm.ContentHeight, availableSize.Height is double.PositiveInfinity ? 0 : availableSize.Height));
    }

    // ── rendering ────────────────────────────────────────────────────────────

    public override void Render(DrawingContext ctx)
    {
        var vm = Editor;
        var editor = vm?.Editor;
        var width = Bounds.Width;
        var height = Bounds.Height;
        ctx.DrawRectangle(new SolidColorBrush(Raised), null, new Rect(0, 0, width, height));
        if (vm is null || editor is null) return;

        var layout = vm.Layout;
        var doc = editor.Document;
        var targets = editor.Targets;
        var laneCount = Math.Max(1, targets.Count);

        DrawLaneBackgrounds(ctx, width, laneCount, targets);
        DrawGrid(ctx, layout, editor, height);
        DrawSections(ctx, layout, doc, vm.SelectedSectionIndex);
        DrawEvents(ctx, layout, doc, targets, vm.SelectedEventIndex);
        DrawRuler(ctx, layout, editor.Duration, width);
        DrawPlayhead(ctx, layout, vm.PositionSeconds, height);
    }

    private static void DrawLaneBackgrounds(DrawingContext ctx, double width, int laneCount, IReadOnlyList<string> targets)
    {
        var line = new Pen(new SolidColorBrush(Border), 1);
        for (var lane = 0; lane <= laneCount; lane++)
        {
            var top = TimelineLayout.LaneTop(lane);
            if (lane % 2 == 0 && lane < laneCount)
            {
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), null, new Rect(0, top, width, TimelineLayout.LaneHeight));
            }
            ctx.DrawLine(line, new Point(0, top), new Point(width, top));
        }

        for (var lane = 0; lane < targets.Count; lane++)
        {
            ctx.DrawRectangle(new SolidColorBrush(Overlay), null, new Rect(0, TimelineLayout.LaneTop(lane), TimelineLayout.GutterWidth, TimelineLayout.LaneHeight));
            DrawText(ctx, Trim(targets[lane], 14), 8, TimelineLayout.LaneTop(lane) + 9, 12, Ink);
        }
        if (targets.Count == 0)
        {
            DrawText(ctx, "Double-click here to add the first event", TimelineLayout.GutterWidth + 12, TimelineLayout.LaneTop(0) + 9, 12, InkMuted);
        }
    }

    private static void DrawGrid(DrawingContext ctx, TimelineLayout layout, ReactiveTimelineEditor editor, double height)
    {
        var faint = new Pen(new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)), 1);
        var step = layout.TickStepSeconds();
        for (var t = 0.0; t <= editor.Duration; t += step)
        {
            var x = layout.TimeToX(t);
            ctx.DrawLine(faint, new Point(x, TimelineLayout.SectionBandTop), new Point(x, height));
        }

        // Beat marks only when zoomed in enough to be told apart.
        if (editor.BeatTimes.Count > 1 && layout.PixelsPerSecond * 0.3 >= 6)
        {
            var beatPen = new Pen(new SolidColorBrush(Color.FromArgb(34, 255, 78, 26)), 1, dashStyle: DashStyle.Dot);
            foreach (var beat in editor.BeatTimes)
            {
                var x = layout.TimeToX(beat);
                ctx.DrawLine(beatPen, new Point(x, TimelineLayout.LanesTop), new Point(x, height));
            }
        }
    }

    private static void DrawRuler(DrawingContext ctx, TimelineLayout layout, double duration, double width)
    {
        ctx.DrawRectangle(new SolidColorBrush(Overlay), null, new Rect(0, 0, width, TimelineLayout.RulerHeight));
        var tick = new Pen(new SolidColorBrush(InkMuted), 1);
        var step = layout.TickStepSeconds();
        for (var t = 0.0; t <= duration + 0.001; t += step)
        {
            var x = layout.TimeToX(t);
            ctx.DrawLine(tick, new Point(x, TimelineLayout.RulerHeight - 7), new Point(x, TimelineLayout.RulerHeight));
            DrawText(ctx, TimelineEditorViewModel.FormatTime(t)[..^3], x + 3, 5, 10, InkMuted);
        }
    }

    private static void DrawSections(DrawingContext ctx, TimelineLayout layout, ReactiveTimelineDocument doc, int selected)
    {
        for (var i = 0; i < doc.Sections.Count; i++)
        {
            var s = doc.Sections[i];
            var rect = new Rect(layout.TimeToX(s.Start), TimelineLayout.SectionBandTop + 3, Math.Max(2, (s.End - s.Start) * layout.PixelsPerSecond), TimelineLayout.SectionBandHeight - 6);
            var isSelected = i == selected;
            var hue = (s.Id.Aggregate(7, (h, c) => (h * 31) + c) & 0x7fffffff) % 6 * 50; // same colour every run (string.GetHashCode isn't stable)
            var fill = ColorFromHue(hue, isSelected ? 0.5 : 0.28);
            ctx.DrawRectangle(new SolidColorBrush(fill), isSelected ? new Pen(new SolidColorBrush(Signal), 2) : new Pen(new SolidColorBrush(Color.FromArgb(120, fill.R, fill.G, fill.B)), 1), rect, 4, 4);
            if (rect.Width > 36)
            {
                DrawText(ctx, Trim(string.IsNullOrWhiteSpace(s.Label) ? s.Id : s.Label, (int)(rect.Width / 7)), rect.X + 6, rect.Y + 5, 11, Ink);
            }
        }
    }

    private static void DrawEvents(DrawingContext ctx, TimelineLayout layout, ReactiveTimelineDocument doc, IReadOnlyList<string> targets, int selected)
    {
        for (var i = 0; i < doc.Timeline.Count; i++)
        {
            var evt = doc.Timeline[i];
            var lane = -1;
            for (var l = 0; l < targets.Count; l++) if (targets[l] == evt.Target) { lane = l; break; }
            if (lane < 0) continue;

            var isSelected = i == selected;
            var x = layout.TimeToX(evt.Time);
            var top = TimelineLayout.LaneTop(lane) + 5;
            var h = TimelineLayout.LaneHeight - 10;
            var accent = isSelected ? Signal : Color.Parse("#7C8CFF");

            if (evt.Duration > 0)
            {
                var rect = new Rect(x, top, layout.EventWidth(evt), h);
                ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(isSelected ? (byte)200 : (byte)120, accent.R, accent.G, accent.B)),
                    new Pen(new SolidColorBrush(accent), isSelected ? 2 : 1), rect, 4, 4);
                // Grip on the right edge, where a drag resizes.
                ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)), 2), new Point(rect.Right - 3, top + 6), new Point(rect.Right - 3, top + h - 6));
                if (rect.Width > 40) DrawText(ctx, Trim(evt.Action, (int)(rect.Width / 7)), x + 6, top + 6, 11, Ink);
            }
            else
            {
                // Instant event: a diamond on the lane's centre line.
                var cy = top + (h / 2);
                var r = 8.0;
                var diamond = new StreamGeometry();
                using (var g = diamond.Open())
                {
                    g.BeginFigure(new Point(x, cy - r), true);
                    g.LineTo(new Point(x + r, cy));
                    g.LineTo(new Point(x, cy + r));
                    g.LineTo(new Point(x - r, cy));
                    g.EndFigure(true);
                }
                ctx.DrawGeometry(new SolidColorBrush(accent), new Pen(new SolidColorBrush(Ink), isSelected ? 2 : 1), diamond);
                DrawText(ctx, Trim(evt.Action, 14), x + r + 4, cy - 7, 11, Ink);
            }
        }
    }

    private static void DrawPlayhead(DrawingContext ctx, TimelineLayout layout, double position, double height)
    {
        var x = layout.TimeToX(position);
        var pen = new Pen(new SolidColorBrush(Signal), 2);
        ctx.DrawLine(pen, new Point(x, 0), new Point(x, height));
        var head = new StreamGeometry();
        using (var g = head.Open())
        {
            g.BeginFigure(new Point(x - 6, 0), true);
            g.LineTo(new Point(x + 6, 0));
            g.LineTo(new Point(x, 10));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(new SolidColorBrush(Signal), null, head);
    }

    // ── input ────────────────────────────────────────────────────────────────

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var vm = Editor;
        var editor = vm?.Editor;
        if (vm is null || editor is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        Focus();
        var p = e.GetPosition(this);
        var hit = vm.Layout.HitTest(p.X, p.Y, editor.Document, editor.Targets);
        var time = vm.Layout.XToTime(p.X);

        switch (hit.Kind)
        {
            case TimelineHitKind.Ruler:
                _drag = Drag.Seek;
                vm.SeekTo(time);
                break;

            case TimelineHitKind.Event or TimelineHitKind.EventEnd:
                vm.SelectEvent(hit.Index);
                _drag = hit.Kind == TimelineHitKind.Event ? Drag.Event : Drag.EventEnd;
                _grabOffset = time - editor.Document.Timeline[hit.Index].Time;
                if (e.ClickCount > 1) { _drag = Drag.None; vm.SeekTo(editor.Document.Timeline[hit.Index].Time); break; }
                vm.BeginDrag();
                break;

            case TimelineHitKind.Section or TimelineHitKind.SectionStart or TimelineHitKind.SectionEnd:
                vm.SelectSection(hit.Index);
                _drag = hit.Kind switch { TimelineHitKind.SectionStart => Drag.SectionStart, TimelineHitKind.SectionEnd => Drag.SectionEnd, _ => Drag.Section };
                _grabOffset = time - editor.Document.Sections[hit.Index].Start;
                vm.BeginDrag();
                break;

            case TimelineHitKind.Lane:
                if (e.ClickCount > 1) vm.AddEventAt(time, hit.Lane);
                else { vm.ClearSelection(); }
                break;

            default:
                vm.ClearSelection();
                break;
        }

        if (_drag != Drag.None) e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var vm = Editor;
        var editor = vm?.Editor;
        if (vm is null || editor is null) return;

        var p = e.GetPosition(this);
        var time = vm.Layout.XToTime(p.X);

        switch (_drag)
        {
            case Drag.Event: vm.DragEvent(time - _grabOffset); return;
            case Drag.EventEnd: vm.DragEventEnd(time); return;
            case Drag.Section: vm.DragSection(time - _grabOffset); return;
            case Drag.SectionStart: vm.DragSectionEdge(true, time); return;
            case Drag.SectionEnd: vm.DragSectionEdge(false, time); return;
            case Drag.Seek: vm.SeekTo(time); return;
        }

        // Not dragging: the cursor tells you what a drag here would do.
        var hover = vm.Layout.HitTest(p.X, p.Y, editor.Document, editor.Targets).Kind;
        Cursor = hover switch
        {
            TimelineHitKind.EventEnd or TimelineHitKind.SectionStart or TimelineHitKind.SectionEnd => ResizeCursor,
            TimelineHitKind.Event or TimelineHitKind.Section => HandCursor,
            _ => ArrowCursor,
        };
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag == Drag.None) return;

        var wasEdit = _drag is Drag.Event or Drag.EventEnd or Drag.Section or Drag.SectionStart or Drag.SectionEnd;
        _drag = Drag.None;
        e.Pointer.Capture(null);
        if (wasEdit) Editor?.EndDrag();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        var vm = Editor;
        if (vm is null || (e.KeyModifiers & KeyModifiers.Control) == 0)
        {
            base.OnPointerWheelChanged(e);
            return; // plain wheel scrolls the surrounding ScrollViewer
        }

        vm.Zoom *= e.Delta.Y > 0 ? 1.15 : 1 / 1.15;
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var vm = Editor;
        if (vm is null) return;

        if (e.Key is Key.Delete or Key.Back) { vm.DeleteCommand.Execute().Subscribe(); e.Handled = true; }
        else if (e.Key == Key.Z && (e.KeyModifiers & KeyModifiers.Control) != 0)
        {
            var redo = (e.KeyModifiers & KeyModifiers.Shift) != 0;
            (redo ? vm.RedoCommand : vm.UndoCommand).Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Y && (e.KeyModifiers & KeyModifiers.Control) != 0) { vm.RedoCommand.Execute().Subscribe(); e.Handled = true; }
        else if (e.Key == Key.S && (e.KeyModifiers & KeyModifiers.Control) != 0) { vm.SaveCommand.Execute().Subscribe(); e.Handled = true; }
        else if (e.Key == Key.D && (e.KeyModifiers & KeyModifiers.Control) != 0) { vm.DuplicateCommand.Execute().Subscribe(); e.Handled = true; }
        else if (e.Key == Key.Space) { vm.PlayPauseCommand.Execute().Subscribe(); e.Handled = true; }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static void DrawText(DrawingContext ctx, string text, double x, double y, double size, Color color) =>
        ctx.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, size, new SolidColorBrush(color)), new Point(x, y));

    private static string Trim(string text, int max) => max < 2 ? "" : text.Length <= max ? text : text[..(max - 1)] + "…";

    private static Color ColorFromHue(double hue, double lightness)
    {
        // Simple HSL to RGB at fixed saturation, good enough for telling sections apart.
        const double s = 0.55;
        var c = (1 - Math.Abs((2 * lightness) - 1)) * s;
        var hp = hue / 60.0 % 6;
        var x = c * (1 - Math.Abs((hp % 2) - 1));
        var (r, g, b) = hp switch { < 1 => (c, x, 0.0), < 2 => (x, c, 0.0), < 3 => (0.0, c, x), < 4 => (0.0, x, c), < 5 => (x, 0.0, c), _ => (c, 0.0, x) };
        var m = lightness - (c / 2);
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
