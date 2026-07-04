using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

/// <summary>Thin floating shell for Song Wars — title bar plus a content host.
/// Owns no state; it just borrows the live SongWarsView's MainContentGrid for
/// the duration of the pop-out (see MainWindow.axaml.cs OpenOrFocusSongWars).</summary>
public partial class SongWarsWindow : Window
{
    /// <summary>Set by the caller; invoked by the title bar's "Dock to Now
    /// Playing sidebar" button (the older, still-supported dock target).</summary>
    public Action? RequestDock { get; set; }

    public SongWarsWindow()
    {
        InitializeComponent();
    }

    public void HostContent(Control content) => Host.Children.Add(content);

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    private void OnMinimize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    private void OnDock(object? sender, RoutedEventArgs e) =>
        RequestDock?.Invoke();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }
}
