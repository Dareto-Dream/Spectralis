using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

/// <summary>Themed yes/no confirmation; defaults to No, matching legacy destructive prompts.</summary>
public partial class ConfirmWindow : Window
{
    private bool _confirmed;

    public ConfirmWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> ShowAsync(
        Window owner,
        string title,
        string message,
        string yesLabel = "Yes",
        string noLabel = "No")
    {
        var window = new ConfirmWindow { Title = title };
        window.MessageText.Text = message;
        window.YesButton.Content = yesLabel;
        window.NoButton.Content = noLabel;
        await window.ShowDialog(owner);
        return window._confirmed;
    }

    private void OnYes(object? sender, RoutedEventArgs e)
    {
        _confirmed = true;
        Close();
    }

    private void OnNo(object? sender, RoutedEventArgs e) => Close();
}
