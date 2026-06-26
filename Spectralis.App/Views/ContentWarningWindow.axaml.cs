using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

public partial class ContentWarningWindow : Window
{
    private readonly TaskCompletionSource<bool> _tcs = new();

    public ContentWarningWindow(string[] tags, string trackName)
    {
        InitializeComponent();
        TrackNameLabel.Text = trackName;
        TagsPanel.ItemsSource = tags;
    }

    public static async Task<bool> PromptAsync(Window owner, string[] tags, string trackName)
    {
        var win = new ContentWarningWindow(tags, trackName);
        await win.ShowDialog(owner);
        return win._tcs.Task.IsCompleted && win._tcs.Task.Result;
    }

    private void OnPlayAnyway(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(true);
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _tcs.TrySetResult(false);
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _tcs.TrySetResult(false);
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _tcs.TrySetResult(true);
            Close();
            e.Handled = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _tcs.TrySetResult(false);
    }
}
