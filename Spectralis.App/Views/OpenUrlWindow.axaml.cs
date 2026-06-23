using Avalonia.Controls;
using Avalonia.Input;
using Spectralis.App.Services;

namespace Spectralis.App.Views;

public partial class OpenUrlWindow : Window
{
    public OpenUrlWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            UrlTextBox.Focus();
            UrlTextBox.SelectAll();
        };
    }

    public string Url
    {
        get => UrlTextBox.Text?.Trim() ?? string.Empty;
        set => UrlTextBox.Text = value;
    }

    public static async Task<string?> PromptAsync(TopLevel topLevel)
    {
        var dialog = new OpenUrlWindow();
        try
        {
            if (topLevel.Clipboard is { } clipboard)
            {
                var clipboardText = await clipboard.GetTextAsync();
                if (OpenUrlService.MightBeUrl(clipboardText))
                {
                    dialog.Url = clipboardText!;
                }
            }
        }
        catch
        {
            // Clipboard prefill is a convenience; opening the dialog matters more.
        }

        if (topLevel is Window owner)
        {
            var result = await dialog.ShowDialog<bool>(owner);
            return result ? dialog.Url : null;
        }

        dialog.Show();
        return null;
    }

    private void OnOpenClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(Url))
        {
            Close(true);
        }
    }

    private void OnCancelClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => Close(false);

    private void OnUrlTextBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(Url))
        {
            e.Handled = true;
            Close(true);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(false);
        }
    }
}
