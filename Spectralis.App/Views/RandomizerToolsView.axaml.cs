using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class RandomizerToolsView : UserControl
{
    private RandomizerToolsViewModel? _vm;

    private double _currentRotation;
    private double _spinVelocity;
    private readonly DispatcherTimer _spinTimer;

    private int _coinFlipTicks;
    private readonly DispatcherTimer _coinTimer;

    private static readonly Color[] WheelColors =
    [
        Color.FromRgb( 59, 130, 246),
        Color.FromRgb( 16, 185, 129),
        Color.FromRgb(245, 158,  11),
        Color.FromRgb(239,  68,  68),
        Color.FromRgb(168,  85, 247),
        Color.FromRgb(236,  72, 153),
        Color.FromRgb( 14, 165, 233),
        Color.FromRgb( 34, 197,  94),
    ];

    public RandomizerToolsView()
    {
        InitializeComponent();

        _spinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnSpinTick);
        _coinTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(75), DispatcherPriority.Render, OnCoinTick);

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) =>
        {
            WheelCanvas.SizeChanged += (_, _) => DrawWheel();
            DrawWheel();
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SpinRequested -= OnSpinRequested;
            _vm.FlipRequested -= OnFlipRequested;
            _vm.WheelEntries.CollectionChanged -= OnEntriesChanged;
            foreach (var entry in _vm.WheelEntries)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        }

        _vm = DataContext as RandomizerToolsViewModel;

        if (_vm is not null)
        {
            _vm.SpinRequested += OnSpinRequested;
            _vm.FlipRequested += OnFlipRequested;
            _vm.WheelEntries.CollectionChanged += OnEntriesChanged;
            foreach (var entry in _vm.WheelEntries)
                entry.PropertyChanged += OnEntryPropertyChanged;
        }

        DrawWheel();
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (WheelEntry entry in e.OldItems)
                entry.PropertyChanged -= OnEntryPropertyChanged;
        if (e.NewItems is not null)
            foreach (WheelEntry entry in e.NewItems)
                entry.PropertyChanged += OnEntryPropertyChanged;

        DrawWheel();
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Text edits flow through the notepad only (WheelEntry.Text never changes in place),
        // but Color/Font/Weight are edited live via the per-entry settings flyout.
        if (e.PropertyName is nameof(WheelEntry.ColorHex) or nameof(WheelEntry.FontFamily) or nameof(WheelEntry.Weight))
            DrawWheel();
    }

    // ── Spin wheel ────────────────────────────────────────────────────────────

    private void OnSpinRequested()
    {
        _spinVelocity = 18.0 + Random.Shared.NextDouble() * 10.0;
        _spinTimer.Start();
    }

    private void OnSpinTick(object? sender, EventArgs e)
    {
        _currentRotation += _spinVelocity;
        _spinVelocity *= 0.981;
        DrawWheel();

        if (_spinVelocity < 0.15)
        {
            _spinTimer.Stop();
            DetermineWinner();
        }
    }

    /// <summary>Cumulative start angle and slice angle (degrees) per entry, sized by relative weight.</summary>
    private static (double[] Start, double[] Slice) GetSliceAngles(IReadOnlyList<WheelEntry> entries)
    {
        int n = entries.Count;
        var start = new double[n];
        var slice = new double[n];
        double total = entries.Sum(e => (double)e.Weight);
        if (total <= 0) total = n;

        double cumulative = 0;
        for (int i = 0; i < n; i++)
        {
            double weight = entries[i].Weight > 0 ? (double)entries[i].Weight : 1.0;
            slice[i] = weight / total * 360.0;
            start[i] = cumulative;
            cumulative += slice[i];
        }
        return (start, slice);
    }

    private void DetermineWinner()
    {
        if (_vm is null || _vm.WheelEntries.Count == 0) return;

        var entries = _vm.WheelEntries.ToList();
        var (start, slice) = GetSliceAngles(entries);
        double normalizedRotation = ((_currentRotation % 360.0) + 360.0) % 360.0;
        double pointerAngle = (360.0 - normalizedRotation) % 360.0;

        int winnerIndex = entries.Count - 1;
        for (int i = 0; i < entries.Count; i++)
        {
            if (pointerAngle >= start[i] && pointerAngle < start[i] + slice[i])
            {
                winnerIndex = i;
                break;
            }
        }

        _vm.FinishSpin(entries[winnerIndex].Text);
    }

    // ── Coin flip ─────────────────────────────────────────────────────────────

    private void OnFlipRequested()
    {
        _coinFlipTicks = 0;
        CoinFaceLabel.Text = "?";
        _coinTimer.Start();
    }

    private void OnCoinTick(object? sender, EventArgs e)
    {
        _coinFlipTicks++;
        bool showHeads = _coinFlipTicks % 2 == 0;
        CoinFaceLabel.Text = showHeads ? "H" : "T";
        CoinVisual.Opacity = 0.55 + (showHeads ? 0.45 : 0.0);

        if (_coinFlipTicks >= 12)
        {
            _coinTimer.Stop();
            _vm?.FinishFlip();
            CoinFaceLabel.Text = _vm?.CoinResult == "Heads" ? "H" : "T";
            CoinVisual.Opacity = 1.0;
        }
    }

    // ── Wheel drawing ─────────────────────────────────────────────────────────

    private (double cx, double cy, double r) WheelDimensions()
    {
        double w = WheelCanvas.Bounds.Width;
        double h = WheelCanvas.Bounds.Height;
        double cx = w / 2;
        double cy = h / 2;
        double r = Math.Min(cx, cy) - 12;
        return (cx, cy, r);
    }

    private void DrawWheel()
    {
        WheelCanvas.Children.Clear();

        var (cx, cy, r) = WheelDimensions();
        if (r < 10) return;

        if (_vm is null || _vm.WheelEntries.Count == 0)
        {
            DrawEmptyState(cx, cy, r);
            return;
        }

        var entries = _vm.WheelEntries.ToList();
        int n = entries.Count;
        var (start, slice) = GetSliceAngles(entries);

        for (int i = 0; i < n; i++)
        {
            double startRad = (_currentRotation + start[i] - 90.0) * Math.PI / 180.0;
            double endRad   = (_currentRotation + start[i] + slice[i] - 90.0) * Math.PI / 180.0;

            double x1 = cx + r * Math.Cos(startRad);
            double y1 = cy + r * Math.Sin(startRad);
            double x2 = cx + r * Math.Cos(endRad);
            double y2 = cy + r * Math.Sin(endRad);

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(cx, cy), true);
                ctx.LineTo(new Point(x1, y1));
                ctx.ArcTo(new Point(x2, y2), new Size(r, r), 0, slice[i] > 180, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }

            WheelCanvas.Children.Add(new ShapePath
            {
                Data = geom,
                Fill = new SolidColorBrush(EntryColor(entries[i], i)),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            });

            double midRad = (_currentRotation + start[i] + slice[i] / 2 - 90.0) * Math.PI / 180.0;
            double lr = r * 0.63;
            double fontSize = Math.Max(9.0, Math.Min(18.0, r * 1.2 * (slice[i] / 360.0) * n));
            double labelWidth = r * 0.5;
            int maxChars = n <= 8 ? 14 : 8;

            var label = new TextBlock
            {
                Text = TruncateEntry(entries[i].Text, maxChars),
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeight.Medium,
                Width = labelWidth,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            if (!string.IsNullOrWhiteSpace(entries[i].FontFamily))
            {
                try { label.FontFamily = new FontFamily(entries[i].FontFamily!); }
                catch { /* invalid font name — keep default */ }
            }
            Canvas.SetLeft(label, cx + lr * Math.Cos(midRad) - labelWidth / 2);
            Canvas.SetTop(label, cy + lr * Math.Sin(midRad) - fontSize / 2);
            WheelCanvas.Children.Add(label);
        }

        double capR = Math.Max(9, r * 0.04);
        var cap = new Ellipse
        {
            Width = capR * 2, Height = capR * 2,
            Fill = new SolidColorBrush(Color.FromRgb(18, 16, 20)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(cap, cx - capR);
        Canvas.SetTop(cap, cy - capR);
        WheelCanvas.Children.Add(cap);

        DrawPointer(cx, r);
    }

    private static Color EntryColor(WheelEntry entry, int index) =>
        !string.IsNullOrWhiteSpace(entry.ColorHex) && Color.TryParse(entry.ColorHex, out var color)
            ? color
            : WheelColors[index % WheelColors.Length];

    private void DrawEmptyState(double cx, double cy, double r)
    {
        var ring = new Ellipse
        {
            Width = r * 2, Height = r * 2,
            Stroke = new SolidColorBrush(Color.FromRgb(70, 62, 75)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromRgb(28, 25, 31)),
            StrokeDashArray = [6, 4],
        };
        Canvas.SetLeft(ring, cx - r);
        Canvas.SetTop(ring, cy - r);
        WheelCanvas.Children.Add(ring);

        var hint = new TextBlock
        {
            Text = "Add entries to spin",
            Foreground = new SolidColorBrush(Color.FromRgb(110, 100, 118)),
            FontSize = 14,
        };
        Canvas.SetLeft(hint, cx - 74);
        Canvas.SetTop(hint, cy - 7);
        WheelCanvas.Children.Add(hint);

        DrawPointer(cx, r);
    }

    private void DrawPointer(double cx, double r)
    {
        double pSize = Math.Max(8, r * 0.05);
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(cx, pSize * 1.5), true);
            ctx.LineTo(new Point(cx - pSize, 0));
            ctx.LineTo(new Point(cx + pSize, 0));
            ctx.EndFigure(true);
        }
        WheelCanvas.Children.Add(new ShapePath
        {
            Data = geom,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.FromRgb(25, 22, 28)),
            StrokeThickness = 1.5,
            IsHitTestVisible = false,
        });
    }

    // ── Handlers ─────────────────────────────────────────────────────────────

    private void OnLoadWheelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SavedWheel wheel && _vm is not null)
            _vm.LoadWheel(wheel);
    }

    private void OnDeleteWheelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SavedWheel wheel && _vm is not null)
            _vm.DeleteWheel(wheel);
    }

    private static string TruncateEntry(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
