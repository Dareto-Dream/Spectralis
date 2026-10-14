using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Spectralis.App.Views;

/// <summary>Floating shell for an existing page. It owns no playback or page state.</summary>
public sealed class PageWindow : Window
{
    public ContentControl PageHost { get; } = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly Border _toolbar;
    private readonly Button _nextDisplay;
    private readonly DispatcherTimer _displayMonitor;
    private WindowState _restoreState;
    private PixelPoint _restorePosition;
    private Size _restoreSize;
    public bool IsPageFullscreen { get; private set; }
    public event Action<bool>? FullscreenChanged;

    public PageWindow(string title)
    {
        Title = title + " — Spectralis"; Width = 1200; Height = 800; MinWidth = 640; MinHeight = 420;
        Background = Avalonia.Media.Brushes.Black;
        var dock = new Button { Content = "Return to app" }; dock.Click += (_, _) => Close();
        _nextDisplay = new Button { Content = "Next display" }; _nextDisplay.Click += (_, _) => MoveToNextDisplay();
        var label = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4,0,18,0) };
        _toolbar = new Border { Padding = new Thickness(12,8), Child = new StackPanel
            { Orientation = Orientation.Horizontal, Spacing = 10, Children = { label, dock, _nextDisplay } } };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        layout.Children.Add(_toolbar); Grid.SetRow(PageHost, 1); layout.Children.Add(PageHost); Content = layout;
        _displayMonitor = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => EnsureVisible());
        Opened += (_, _) => { EnsureVisible(); _displayMonitor.Start(); };
        Closed += (_, _) => { _displayMonitor.Stop(); if (IsPageFullscreen) SetFullscreen(false); };
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape && IsPageFullscreen) { SetFullscreen(false); e.Handled = true; }
        }, handledEventsToo: false);
    }

    public void SetFullscreen(bool enabled)
    {
        if (enabled == IsPageFullscreen) return;
        if (enabled)
        {
            _restoreState = WindowState; _restorePosition = Position; _restoreSize = new Size(Width, Height);
            IsPageFullscreen = true; _toolbar.IsVisible = false; WindowState = WindowState.FullScreen;
        }
        else
        {
            IsPageFullscreen = false; WindowState = WindowState.Normal;
            Position = _restorePosition; Width = _restoreSize.Width; Height = _restoreSize.Height;
            WindowState = _restoreState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            _toolbar.IsVisible = true;
        }
        FullscreenChanged?.Invoke(enabled);
    }

    private void MoveToNextDisplay()
    {
        var screens = Screens.All.ToList(); if (screens.Count < 2) return;
        var next = screens[(screens.IndexOf(Screens.ScreenFromWindow(this)!) + 1) % screens.Count];
        if (IsPageFullscreen) SetFullscreen(false);
        WindowState = WindowState.Normal;
        Width = Math.Min(Width, next.WorkingArea.Width / next.Scaling);
        Height = Math.Min(Height, next.WorkingArea.Height / next.Scaling);
        Position = next.WorkingArea.Position;
    }
    private void EnsureVisible()
    {
        _nextDisplay.IsVisible = Screens.All.Count > 1;
        if (WindowState == WindowState.Minimized || Screens.All.Count == 0) return;
        if (Screens.All.Any(s => s.Bounds.Contains(new PixelPoint(Position.X + 40, Position.Y + 16)))) return;
        if (IsPageFullscreen) SetFullscreen(false);
        var screen = Screens.Primary ?? Screens.All[0]; WindowState = WindowState.Normal;
        Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling);
        Height = Math.Min(Height, screen.WorkingArea.Height / screen.Scaling); Position = screen.WorkingArea.Position;
    }
}
