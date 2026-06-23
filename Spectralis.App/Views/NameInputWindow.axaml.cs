using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

/// <summary>Small themed name prompt used by playlist creation and save-as flows.</summary>
public partial class NameInputWindow : Window
{
    private string? _result;

    public NameInputWindow()
    {
        InitializeComponent();
        Opened += (_, _) => NameBox.Focus();
    }

    public static async Task<string?> PromptAsync(Window owner, string title, string prompt, string initial = "")
    {
        var window = new NameInputWindow { Title = title };
        window.PromptText.Text = prompt;
        window.NameBox.Text = initial;
        await window.ShowDialog(owner);
        return window._result;
    }

    private void OnNameBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Accept();
        }
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Accept();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void Accept()
    {
        var text = NameBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        _result = text;
        Close();
    }
}
