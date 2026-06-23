using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spectralis.Core.ContentWarnings;

namespace Spectralis.App.Views;

public partial class ContentWarningEditorWindow : Window
{
    private readonly string _filePath;

    public ContentWarningEditorWindow(string filePath)
    {
        _filePath = filePath;
        InitializeComponent();

        TrackNameLabel.Text = Path.GetFileNameWithoutExtension(filePath);

        var existing = TrackContentWarningStore.Get(filePath);
        TagsBox.Text = string.Join(", ", existing);
    }

    public static async Task ShowAsync(Window owner, string filePath)
    {
        var win = new ContentWarningEditorWindow(filePath);
        await win.ShowDialog(owner);
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var raw = TagsBox.Text ?? string.Empty;
        var tags = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        TrackContentWarningStore.Set(_filePath, tags);
        Close();
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        TrackContentWarningStore.Clear(_filePath);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
