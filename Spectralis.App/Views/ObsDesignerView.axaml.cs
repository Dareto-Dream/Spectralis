using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Spectralis.App.ViewModels;
using Spectralis.Core.Integrations.Obs;

namespace Spectralis.App.Views;

public partial class ObsDesignerView : UserControl
{
    private const double HandleSize = 8;

    private double CanvasW { get; set; } = 640;
    private double CanvasH { get; set; } = 360;
    private double _aspectRatio = 16.0 / 9.0;

    private ObsEditorViewModel? _vm;

    // Drag/resize state
    private ObsDesignerItem? _dragItem;
    private bool _isDragging;
    private bool _isResizing;
    private Avalonia.Point _dragStart;
    private double _dragOriginX, _dragOriginY, _dragOriginW, _dragOriginH;
    private int _resizeHandle;

    private ObsDesignerItem? _selected;
    private readonly Dictionary<ObsDesignerItem, Rectangle> _rects = [];
    private readonly Dictionary<ObsDesignerItem, TextBlock> _labels = [];

    private static readonly IBrush[] WidgetColors =
    [
        new SolidColorBrush(Color.FromArgb(180, 59,  130, 246)),
        new SolidColorBrush(Color.FromArgb(180, 16,  185, 129)),
        new SolidColorBrush(Color.FromArgb(180, 245, 158,  11)),
        new SolidColorBrush(Color.FromArgb(180, 239,  68,  68)),
        new SolidColorBrush(Color.FromArgb(180, 168,  85, 247)),
        new SolidColorBrush(Color.FromArgb(180, 236,  72, 153)),
    ];

    private static readonly string[] WidgetTypeOrder =
    [
        ObsWidgetType.NowPlaying, ObsWidgetType.Lyrics, ObsWidgetType.Queue,
        ObsWidgetType.Progress, ObsWidgetType.Visualizer, ObsWidgetType.SongWarsBracket,
    ];

    // ─── Public API ───────────────────────────────────────────────────────────
    public ObservableCollection<ObsDesignerItem> Items { get; } = [];
    public bool AutoApply { get; set; }

    public event EventHandler<ObsDesignerItem?>? SelectedItemChanged;
    public event EventHandler<string?>? StatusChanged;

    public ObsDesignerView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.LayoutRefreshRequested -= OnLayoutRefreshRequested;

        if (DataContext is not ObsEditorViewModel vm) return;
        _vm = vm;
        _vm.LayoutRefreshRequested += OnLayoutRefreshRequested;
        ApplyCanvasAspectRatio(vm.CanvasAspectRatio);
        LoadLayout(vm.GetCurrentLayout());
    }

    private void OnLayoutRefreshRequested(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        ApplyCanvasAspectRatio(_vm.CanvasAspectRatio);
        LoadLayout(_vm.GetCurrentLayout());
    }

    // ─── Canvas sizing ────────────────────────────────────────────────────────
    private void ApplyCanvasAspectRatio(double aspectRatio)
    {
        _aspectRatio = aspectRatio <= 0 || double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio)
            ? 16.0 / 9.0
            : aspectRatio;
        FitCanvasToBorder();
    }

    private void OnDesignerBorderSizeChanged(object? sender, SizeChangedEventArgs e) => FitCanvasToBorder();

    private void FitCanvasToBorder()
    {
        var bw = DesignerBorder.Bounds.Width;
        var bh = DesignerBorder.Bounds.Height;
        if (bw <= 0) return;

        var maxW = bw;
        var maxH = bh > 0 ? bh : maxW / _aspectRatio;

        if (_aspectRatio >= maxW / maxH)
        {
            CanvasW = maxW;
            CanvasH = maxW / _aspectRatio;
        }
        else
        {
            CanvasH = maxH;
            CanvasW = maxH * _aspectRatio;
        }

        DesignerCanvas.Width = CanvasW;
        DesignerCanvas.Height = CanvasH;

        foreach (var item in Items)
            PlaceRect(item);
    }

    // ─── Layout load/save ─────────────────────────────────────────────────────
    public void LoadLayout(ObsLayout layout)
    {
        Items.Clear();
        _rects.Clear();
        _labels.Clear();
        DesignerCanvas.Children.Clear();
        SetSelected(null);

        foreach (var widget in layout.Widgets)
        {
            var item = new ObsDesignerItem(widget);
            Items.Add(item);
            AddWidgetRect(item);
        }
    }

    public ObsLayout CollectLayout()
    {
        foreach (var item in Items)
            item.CommitToSource();
        return new ObsLayout { Widgets = Items.Select(i => i.Source.Clone()).ToList() };
    }

    // ─── Public widget manipulation ───────────────────────────────────────────
    public void AddWidget(string type)
    {
        var widget = new ObsLayoutWidget
        {
            Type = type,
            X = 0.05, Y = 0.05, W = 0.30, H = 0.12,
            ShowArt = true, ShowArtist = true, ShowProgress = true,
        };
        var item = new ObsDesignerItem(widget);
        Items.Add(item);
        AddWidgetRect(item);
        SetSelected(item);
        if (AutoApply) TriggerAutoApply();
    }

    public void RemoveSelectedWidget()
    {
        if (_selected is null) return;
        if (_rects.TryGetValue(_selected, out var r)) DesignerCanvas.Children.Remove(r);
        if (_labels.TryGetValue(_selected, out var l)) DesignerCanvas.Children.Remove(l);
        _rects.Remove(_selected);
        _labels.Remove(_selected);
        Items.Remove(_selected);
        SetSelected(null);
        if (AutoApply) TriggerAutoApply();
    }

    public void SelectItem(ObsDesignerItem? item) => SetSelected(item);

    private void TriggerAutoApply()
    {
        var layout = CollectLayout();
        _vm?.ApplyDesignerLayout(layout);
    }

    // ─── Widget rendering ─────────────────────────────────────────────────────
    private void AddWidgetRect(ObsDesignerItem item)
    {
        var colorIndex = Array.IndexOf(WidgetTypeOrder, item.Source.Type);
        var fill = WidgetColors[Math.Clamp(colorIndex < 0 ? 0 : colorIndex, 0, WidgetColors.Length - 1)];

        var rect = new Rectangle
        {
            Fill = fill,
            Stroke = Brushes.White,
            StrokeThickness = 1,
            Cursor = new Cursor(StandardCursorType.SizeAll),
            Tag = item,
        };
        var label = new TextBlock
        {
            Text = item.Label.ToUpperInvariant(),
            Foreground = Brushes.White,
            FontSize = 11,
            IsHitTestVisible = false,
        };

        _rects[item] = rect;
        _labels[item] = label;
        DesignerCanvas.Children.Add(rect);
        DesignerCanvas.Children.Add(label);
        PlaceRect(item);
    }

    private void PlaceRect(ObsDesignerItem item)
    {
        if (!_rects.TryGetValue(item, out var rect) || !_labels.TryGetValue(item, out var lbl)) return;

        double px = item.X * CanvasW;
        double py = item.Y * CanvasH;
        double pw = item.W * CanvasW;
        double ph = item.H * CanvasH;

        rect.Width  = pw;
        rect.Height = ph;
        Canvas.SetLeft(rect, px);
        Canvas.SetTop(rect, py);

        Canvas.SetLeft(lbl, px + 4);
        Canvas.SetTop(lbl, py + 4);
    }

    private void SetSelected(ObsDesignerItem? item)
    {
        if (_selected is not null && _rects.TryGetValue(_selected, out var old))
            old.StrokeThickness = 1;

        _selected = item;

        if (item is not null && _rects.TryGetValue(item, out var sel))
            sel.StrokeThickness = 2;

        SelectedItemChanged?.Invoke(this, item);
    }

    // ─── Pointer events ───────────────────────────────────────────────────────
    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(DesignerCanvas);
        _dragStart = pos;

        foreach (var item in Enumerable.Reverse(Items))
        {
            double px = item.X * CanvasW, py = item.Y * CanvasH;
            double pw = item.W * CanvasW, ph = item.H * CanvasH;

            var handle = HitHandle(pos, px, py, pw, ph);
            if (handle >= 0)
            {
                SetSelected(item);
                _dragItem = item;
                _dragOriginX = item.X; _dragOriginY = item.Y;
                _dragOriginW = item.W; _dragOriginH = item.H;
                _isResizing = true;
                _resizeHandle = handle;
                e.Pointer.Capture(DesignerCanvas);
                return;
            }

            if (pos.X >= px && pos.X <= px + pw && pos.Y >= py && pos.Y <= py + ph)
            {
                SetSelected(item);
                _dragItem = item;
                _dragOriginX = item.X; _dragOriginY = item.Y;
                _isDragging = true;
                e.Pointer.Capture(DesignerCanvas);
                return;
            }
        }

        SetSelected(null);
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragItem is null) return;
        var pos = e.GetPosition(DesignerCanvas);
        double dx = (pos.X - _dragStart.X) / CanvasW;
        double dy = (pos.Y - _dragStart.Y) / CanvasH;

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
        var statusText =
            $"{_dragItem.Label.ToUpperInvariant()}  " +
            $"x={_dragItem.X:P0}  y={_dragItem.Y:P0}  w={_dragItem.W:P0}  h={_dragItem.H:P0}";
        StatusChanged?.Invoke(this, statusText);
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _isResizing = false;
        _dragItem = null;
        e.Pointer.Capture(null);
        StatusChanged?.Invoke(this, null);

        if (AutoApply) TriggerAutoApply();
    }

    private void ApplyResize(ObsDesignerItem item, double dx, double dy)
    {
        switch (_resizeHandle)
        {
            case 0:
                item.X = Math.Clamp(_dragOriginX + dx, 0, _dragOriginX + _dragOriginW - 0.02);
                item.Y = Math.Clamp(_dragOriginY + dy, 0, _dragOriginY + _dragOriginH - 0.02);
                item.W = _dragOriginX + _dragOriginW - item.X;
                item.H = _dragOriginY + _dragOriginH - item.Y;
                break;
            case 1:
                item.Y = Math.Clamp(_dragOriginY + dy, 0, _dragOriginY + _dragOriginH - 0.02);
                item.W = Math.Clamp(_dragOriginW + dx, 0.02, 1 - _dragOriginX);
                item.H = _dragOriginY + _dragOriginH - item.Y;
                break;
            case 2:
                item.W = Math.Clamp(_dragOriginW + dx, 0.02, 1 - _dragOriginX);
                item.H = Math.Clamp(_dragOriginH + dy, 0.02, 1 - _dragOriginY);
                break;
            case 3:
                item.X = Math.Clamp(_dragOriginX + dx, 0, _dragOriginX + _dragOriginW - 0.02);
                item.W = _dragOriginX + _dragOriginW - item.X;
                item.H = Math.Clamp(_dragOriginH + dy, 0.02, 1 - _dragOriginY);
                break;
        }
    }

    private static int HitHandle(Avalonia.Point pos, double px, double py, double pw, double ph)
    {
        double hs = HandleSize;
        Avalonia.Rect[] handles =
        [
            new(px - hs/2, py - hs/2, hs, hs),
            new(px + pw - hs/2, py - hs/2, hs, hs),
            new(px + pw - hs/2, py + ph - hs/2, hs, hs),
            new(px - hs/2, py + ph - hs/2, hs, hs),
        ];
        for (int i = 0; i < handles.Length; i++)
            if (handles[i].Contains(pos)) return i;
        return -1;
    }
}
