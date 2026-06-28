using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Input;
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
        Loaded += (_, _) => DrawWheel();
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.SpinRequested -= OnSpinRequested;
            _vm.FlipRequested -= OnFlipRequested;
            _vm.WheelEntries.CollectionChanged -= OnEntriesChanged;
        }

        _vm = DataContext as RandomizerToolsViewModel;

        if (_vm is not null)
        {
            _vm.SpinRequested += OnSpinRequested;
            _vm.FlipRequested += OnFlipRequested;
            _vm.WheelEntries.CollectionChanged += OnEntriesChanged;
        }

        DrawWheel();
    }

    private void OnEntriesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => DrawWheel();

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

    private void DetermineWinner()
    {
        if (_vm is null || _vm.WheelEntries.Count == 0) return;

        var entries = _vm.WheelEntries.ToList();
        int n = entries.Count;
        double sliceAngle = 360.0 / n;
        double normalizedRotation = ((_currentRotation % 360.0) + 360.0) % 360.0;
        double pointerAngle = (360.0 - normalizedRotation) % 360.0;
        int winnerIndex = (int)(pointerAngle / sliceAngle) % n;

        _vm.FinishSpin(entries[winnerIndex]);
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

    private void DrawWheel()
    {
        WheelCanvas.Children.Clear();

        if (_vm is null || _vm.WheelEntries.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        var entries = _vm.WheelEntries.ToList();
        int n = entries.Count;
        double sliceAngle = 360.0 / n;
        const double cx = 110, cy = 110, r = 104;

        for (int i = 0; i < n; i++)
        {
            double startRad = (_currentRotation + i * sliceAngle - 90.0) * Math.PI / 180.0;
            double endRad   = (_currentRotation + (i + 1) * sliceAngle - 90.0) * Math.PI / 180.0;

            double x1 = cx + r * Math.Cos(startRad);
            double y1 = cy + r * Math.Sin(startRad);
            double x2 = cx + r * Math.Cos(endRad);
            double y2 = cy + r * Math.Sin(endRad);

            var geom = new StreamGeometry();
            using (var ctx = geom.Open())
            {
                ctx.BeginFigure(new Point(cx, cy), true);
                ctx.LineTo(new Point(x1, y1));
                ctx.ArcTo(new Point(x2, y2), new Size(r, r), 0, sliceAngle > 180, SweepDirection.Clockwise);
                ctx.EndFigure(true);
            }

            WheelCanvas.Children.Add(new ShapePath
            {
                Data = geom,
                Fill = new SolidColorBrush(WheelColors[i % WheelColors.Length]),
                Stroke = Brushes.White,
                StrokeThickness = 1.5,
            });

            double midRad  = (_currentRotation + (i + 0.5) * sliceAngle - 90.0) * Math.PI / 180.0;
            double lr = r * 0.63;
            double fontSize = Math.Max(8.0, Math.Min(12.0, 160.0 / n));
            int maxChars = n <= 8 ? 10 : 6;

            var label = new TextBlock
            {
                Text = TruncateEntry(entries[i], maxChars),
                Foreground = Brushes.White,
                FontSize = fontSize,
                FontWeight = FontWeight.Medium,
                Width = 56,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, cx + lr * Math.Cos(midRad) - 28);
            Canvas.SetTop(label, cy + lr * Math.Sin(midRad) - fontSize / 2);
            WheelCanvas.Children.Add(label);
        }

        // Center cap
        var cap = new Ellipse
        {
            Width = 18, Height = 18,
            Fill = new SolidColorBrush(Color.FromRgb(18, 16, 20)),
            Stroke = Brushes.White,
            StrokeThickness = 2,
        };
        Canvas.SetLeft(cap, cx - 9);
        Canvas.SetTop(cap, cy - 9);
        WheelCanvas.Children.Add(cap);

        DrawPointer(cx);
    }

    private void DrawEmptyState()
    {
        var ring = new Ellipse
        {
            Width = 208, Height = 208,
            Stroke = new SolidColorBrush(Color.FromRgb(70, 62, 75)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromRgb(28, 25, 31)),
            StrokeDashArray = [6, 4],
        };
        Canvas.SetLeft(ring, 6);
        Canvas.SetTop(ring, 6);
        WheelCanvas.Children.Add(ring);

        var hint = new TextBlock
        {
            Text = "Add entries to spin",
            Foreground = new SolidColorBrush(Color.FromRgb(110, 100, 118)),
            FontSize = 13,
        };
        Canvas.SetLeft(hint, 48);
        Canvas.SetTop(hint, 102);
        WheelCanvas.Children.Add(hint);

        DrawPointer(110);
    }

    private void DrawPointer(double cx)
    {
        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(new Point(cx, 9), true);
            ctx.LineTo(new Point(cx - 9, 0));
            ctx.LineTo(new Point(cx + 9, 0));
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

    private void OnRemoveEntryClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is string entry && _vm is not null)
            _vm.RemoveEntry(entry);
    }

    private void OnNewEntryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _vm is not null)
        {
            _vm.TryAddEntry();
            e.Handled = true;
        }
    }

    private static string TruncateEntry(string s, int max) =>
        s.Length <= max ? s : s[..max];
}
