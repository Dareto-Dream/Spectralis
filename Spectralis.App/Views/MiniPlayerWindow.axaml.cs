using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Spectralis.App.Views;

public partial class MiniPlayerWindow : Window
{
    /// <summary>Raised when the user asks to return to the full player.</summary>
    public event EventHandler? ExpandRequested;

    public MiniPlayerWindow()
    {
        InitializeComponent();
        Topmost = true;
        PinToggle.IsChecked = true;
    }

    private void OnDragWindow(object? sender, PointerPressedEventArgs e)
    {
        // Any non-interactive surface drags the window.
        if (e.Source is Control { } source &&
            source.FindAncestorOfType<Button>() is null &&
            source.FindAncestorOfType<Slider>() is null &&
            source.FindAncestorOfType<ToggleButton>() is null)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnPinChanged(object? sender, RoutedEventArgs e)
    {
        Topmost = PinToggle.IsChecked == true;
    }

    private void OnExpand(object? sender, RoutedEventArgs e)
    {
        ExpandRequested?.Invoke(this, EventArgs.Empty);
    }
}
