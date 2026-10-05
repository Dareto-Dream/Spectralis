using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class NetworkTabView : UserControl
{
    public NetworkTabView()
    {
        InitializeComponent();
        var vm = new NetworkLogViewModel();
        DataContext = vm;
        // The list refreshes on a timer, so it only runs while the tab is showing. Closing the window ends it for good
        // and switches a paused log back on.
        AttachedToVisualTree += (_, _) =>
        {
            vm.Activate();
            if (TopLevel.GetTopLevel(this) is Window window)
            {
                window.Closed -= OnWindowClosed;
                window.Closed += OnWindowClosed;
            }
        };
        DetachedFromVisualTree += (_, _) => vm.Deactivate();
    }

    private void OnWindowClosed(object? sender, EventArgs e) => (DataContext as NetworkLogViewModel)?.Dispose();

    private void OnClear(object? sender, RoutedEventArgs e) => (DataContext as NetworkLogViewModel)?.Clear();

    private async void OnCopy(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not NetworkLogViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        try { await clipboard.SetTextAsync(vm.BuildReport()); }
        catch { /* clipboard busy; nothing useful to tell the user */ }
    }
}
