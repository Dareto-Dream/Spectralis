using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Spectralis.Core.Integrations.Obs;

namespace Spectralis.App.Views;

public sealed class DeadZoneItem
{
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; }
    public double H { get; set; }
    public string? Label { get; set; }
}

public partial class DeadZoneDesignerView : UserControl
{
    private const double HandleSize = 8;
    private const double MinZoneSize = 0.02;

    private double _canvasW = 640;
    private double _canvasH = 360;
    private double _aspectRatio = 16.0 / 9.0;

    private readonly List<DeadZoneItem> _items = [];
    private readonly Dictionary<DeadZoneItem, Rectangle> _rects = [];

    private DeadZoneItem? _selected;
    private DeadZoneItem? _dragItem;
    private DeadZoneItem? _drawingZone;

    private bool _isDragging;
    private bool _isResizing;
    private bool _isDrawing;

    private Avalonia.Point _dragStart;
    private double _dragOriginX, _dragOriginY, _dragOriginW, _dragOriginH;
    private int _resizeHandle;

    private static readonly IBrush ZoneFill           = new SolidColorBrush(Color.FromArgb(80,  239, 68,  68));
    private static readonly IBrush ZoneStroke         = new SolidColorBrush(Color.FromArgb(180, 239, 68,  68));
    private static readonly IBrush ZoneSelectedStroke = new SolidColorBrush(Color.FromArgb(255, 248, 113, 113));

    public event EventHandler? ZonesChanged;
    public event EventHandler<DeadZoneItem?>? SelectedItemChanged;

    public bool HasSelection => _selected is not null;

    public DeadZoneDesignerView()
    {
        InitializeComponent();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public void SetAspectRatio(double ratio)
    {
        _aspectRatio = ratio <= 0 || double.IsNaN(ratio) || double.IsInfinity(ratio)
            ? 16.0 / 9.0
            : ratio;
        FitCanvasToBorder();
    }

    public void LoadZones(IEnumerable<DeadZone> zones)
    {
        _items.Clear();
        _rects.Clear();
        DesignerCanvas.Children.Clear();
        SetSelected(null);

        foreach (var z in zones)
        {
            var item = new DeadZoneItem { X = z.X, Y = z.Y, W = z.W, H = z.H, Label = z.Label };
            _items.Add(item);
            AddRect(item);
        }
    }

    public IReadOnlyList<DeadZone> CollectZones() =>
        _items.Select(i => new DeadZone { X = i.X, Y = i.Y, W = i.W, H = i.H, Label = i.Label })
              .ToList();

    public void RemoveSelected()
    {
        if (_selected is null) return;
        if (_rects.TryGetValue(_selected, out var r)) DesignerCanvas.Children.Remove(r);
        _rects.Remove(_selected);
        _items.Remove(_selected);
        SetSelected(null);
        ZonesChanged?.Invoke(this, EventArgs.Empty);
    }

    // ─── Canvas sizing ────────────────────────────────────────────────────────

    private void OnDesignerBorderSizeChanged(object? sender, SizeChangedEventArgs e) =>
        FitCanvasToBorder();

    private void FitCanvasToBorder()
    {
        var bw = DesignerBorder.Bounds.Width;
        var bh = DesignerBorder.Bounds.Height;
        if (bw <= 0) return;

        var maxW = bw;
        var maxH = bh > 0 ? bh : maxW / _aspectRatio;

        if (_aspectRatio >= maxW / maxH)
        {
            _canvasW = maxW;
            _canvasH = maxW / _aspectRatio;
        }
        else
        {
            _canvasH = maxH;
            _canvasW = maxH * _aspectRatio;
        }

        DesignerCanvas.Width  = _canvasW;
        DesignerCanvas.Height = _canvasH;

        foreach (var item in _items)
            PlaceRect(item);
    }

    // ─── Rect management ─────────────────────────────────────────────────────

    private void AddRect(DeadZoneItem item)
    {
        var rect = new Rectangle
        {
            Fill            = ZoneFill,
            Stroke          = ZoneStroke,
            StrokeThickness = 1.5,
            Cursor          = new Cursor(StandardCursorType.SizeAll),
            Tag             = item,
        };
        _rects[item] = rect;
        DesignerCanvas.Children.Add(rect);
        PlaceRect(item);
    }

    private void PlaceRect(DeadZoneItem item)
    {
        if (!_rects.TryGetValue(item, out var rect)) return;
        rect.Width  = Math.Max(1, item.W * _canvasW);
        rect.Height = Math.Max(1, item.H * _canvasH);
        Canvas.SetLeft(rect, item.X * _canvasW);
        Canvas.SetTop(rect,  item.Y * _canvasH);
    }

    private void SetSelected(DeadZoneItem? item)
    {
        if (_selected is not null && _rects.TryGetValue(_selected, out var old))
        {
            old.Stroke          = ZoneStroke;
            old.StrokeThickness = 1.5;
        }
        _selected = item;
        if (item is not null && _rects.TryGetValue(item, out var sel))
        {
            sel.Stroke          = ZoneSelectedStroke;
            sel.StrokeThickness = 2.5;
        }
        SelectedItemChanged?.Invoke(this, item);
    }

    // ─── Pointer events ───────────────────────────────────────────────────────

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(DesignerCanvas);
        _dragStart = pos;

        // Resize handle on selected item
        if (_selected is not null)
        {
            double px = _selected.X * _canvasW, py = _selected.Y * _canvasH;
            double pw = _selected.W * _canvasW, ph = _selected.H * _canvasH;
            var h = HitHandle(pos, px, py, pw, ph);
            if (h >= 0)
            {
                _dragItem    = _selected;
                _dragOriginX = _selected.X; _dragOriginY = _selected.Y;
                _dragOriginW = _selected.W; _dragOriginH = _selected.H;
                _isResizing  = true;
                _resizeHandle = h;
                e.Pointer.Capture(DesignerCanvas);
                return;
            }
        }

        // Hit test existing zones (reverse order — top-most first)
        for (var i = _items.Count - 1; i >= 0; i--)
        {
            var item = _items[i];
            double px = item.X * _canvasW, py = item.Y * _canvasH;
            double pw = item.W * _canvasW, ph = item.H * _canvasH;
            if (pos.X >= px && pos.X <= px + pw && pos.Y >= py && pos.Y <= py + ph)
            {
                SetSelected(item);
                _dragItem    = item;
                _dragOriginX = item.X; _dragOriginY = item.Y;
                _isDragging  = true;
                e.Pointer.Capture(DesignerCanvas);
                return;
            }
        }

        // Start drawing a new zone
        SetSelected(null);
        _drawingZone = new DeadZoneItem
        {
            X = Math.Clamp(pos.X / _canvasW, 0, 1),
            Y = Math.Clamp(pos.Y / _canvasH, 0, 1),
            W = 0,
            H = 0,
        };
        _isDrawing = true;
        e.Pointer.Capture(DesignerCanvas);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(DesignerCanvas);

        if (_isDrawing && _drawingZone is not null)
        {
            var x0 = _dragStart.X / _canvasW;
            var y0 = _dragStart.Y / _canvasH;
            var x1 = Math.Clamp(pos.X / _canvasW, 0, 1);
            var y1 = Math.Clamp(pos.Y / _canvasH, 0, 1);
            _drawingZone.X = Math.Min(x0, x1);
            _drawingZone.Y = Math.Min(y0, y1);
            _drawingZone.W = Math.Abs(x1 - x0);
            _drawingZone.H = Math.Abs(y1 - y0);

            if (!_rects.ContainsKey(_drawingZone))
                AddRect(_drawingZone);
            else
                PlaceRect(_drawingZone);
            return;
        }

        if (_dragItem is null) return;

        double dx = (pos.X - _dragStart.X) / _canvasW;
        double dy = (pos.Y - _dragStart.Y) / _canvasH;

        if (_isDragging)
        {
            _dragItem.X = Math.Clamp(_dragOriginX + dx, 0, 1 - _dragItem.W);
            _dragItem.Y = Math.Clamp(_dragOriginY + dy, 0, 1 - _dragItem.H);
        }
        else if (_isResizing)
        {
            ApplyResize(_dragItem, dx, dy);
        }

        PlaceRect(_dragItem);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        e.Pointer.Capture(null);

        if (_isDrawing && _drawingZone is not null)
        {
            _isDrawing = false;
            if (_drawingZone.W >= MinZoneSize && _drawingZone.H >= MinZoneSize)
            {
                _items.Add(_drawingZone);
                SetSelected(_drawingZone);
                ZonesChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                // too small — discard
                if (_rects.TryGetValue(_drawingZone, out var r)) DesignerCanvas.Children.Remove(r);
                _rects.Remove(_drawingZone);
            }
            _drawingZone = null;
            return;
        }

        if (_isDragging || _isResizing)
            ZonesChanged?.Invoke(this, EventArgs.Empty);

        _isDragging = false;
        _isResizing = false;
        _dragItem   = null;
    }

    // ─── Resize ───────────────────────────────────────────────────────────────

    private void ApplyResize(DeadZoneItem item, double dx, double dy)
    {
        switch (_resizeHandle)
        {
            case 0: // top-left
                item.X = Math.Clamp(_dragOriginX + dx, 0, _dragOriginX + _dragOriginW - MinZoneSize);
                item.Y = Math.Clamp(_dragOriginY + dy, 0, _dragOriginY + _dragOriginH - MinZoneSize);
                item.W = _dragOriginX + _dragOriginW - item.X;
                item.H = _dragOriginY + _dragOriginH - item.Y;
                break;
            case 1: // top-right
                item.Y = Math.Clamp(_dragOriginY + dy, 0, _dragOriginY + _dragOriginH - MinZoneSize);
                item.W = Math.Clamp(_dragOriginW + dx, MinZoneSize, 1 - _dragOriginX);
                item.H = _dragOriginY + _dragOriginH - item.Y;
                break;
            case 2: // bottom-right
                item.W = Math.Clamp(_dragOriginW + dx, MinZoneSize, 1 - _dragOriginX);
                item.H = Math.Clamp(_dragOriginH + dy, MinZoneSize, 1 - _dragOriginY);
                break;
            case 3: // bottom-left
                item.X = Math.Clamp(_dragOriginX + dx, 0, _dragOriginX + _dragOriginW - MinZoneSize);
                item.W = _dragOriginX + _dragOriginW - item.X;
                item.H = Math.Clamp(_dragOriginH + dy, MinZoneSize, 1 - _dragOriginY);
                break;
        }
    }

    private static int HitHandle(Avalonia.Point pos, double px, double py, double pw, double ph)
    {
        double hs = HandleSize;
        Avalonia.Rect[] handles =
        [
            new(px - hs / 2,       py - hs / 2,       hs, hs),
            new(px + pw - hs / 2,  py - hs / 2,       hs, hs),
            new(px + pw - hs / 2,  py + ph - hs / 2,  hs, hs),
            new(px - hs / 2,       py + ph - hs / 2,  hs, hs),
        ];
        for (var i = 0; i < handles.Length; i++)
            if (handles[i].Contains(pos)) return i;
        return -1;
    }
}
