using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

public sealed record CloseToTrayPromptResult(bool Accepted, bool CloseToTray);

public partial class CloseToTrayPromptWindow : Window
{
    private CloseToTrayPromptResult _result = new(false, true);

    public CloseToTrayPromptWindow()
    {
        InitializeComponent();
    }

    public static async Task<CloseToTrayPromptResult> ShowAsync(Window owner, bool closeToTray)
    {
        var window = new CloseToTrayPromptWindow();
        window.TaskbarBehaviorCheck.IsChecked = closeToTray;
        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnOkDontRemind(object? sender, RoutedEventArgs e)
    {
        _result = new CloseToTrayPromptResult(true, TaskbarBehaviorCheck.IsChecked == true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
