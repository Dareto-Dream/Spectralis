using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Views;

/// <summary>Picks a playlist's default visualizer from the same option list NowPlayingViewModel
/// already builds (catalog + scripted + installed). Mirrors PlaylistPickerWindow's minimal style.</summary>
public partial class VisualizerPickerWindow : Window
{
    private bool _confirmed;
    private VisualizerRef? _result;

    public VisualizerPickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>Confirmed is false on cancel (no change intended). Confirmed true with a null
    /// Result means "Clear" was pressed — the playlist should stop having a default visualizer.</summary>
    public static async Task<(bool Confirmed, VisualizerRef? Result)> PromptAsync(Window owner, IEnumerable<VisualizerOption> options)
    {
        var window = new VisualizerPickerWindow();
        window.OptionList.ItemsSource = options.ToList();
        await window.ShowDialog(owner);
        return (window._confirmed, window._result);
    }

    private void OnSet(object? sender, RoutedEventArgs e)
    {
        if (OptionList.SelectedItem is VisualizerOption option)
        {
            _result = ToVisualizerRef(option);
            _confirmed = true;
            Close();
        }
    }

    private void OnClear(object? sender, RoutedEventArgs e)
    {
        _result = null;
        _confirmed = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnListDoubleTapped(object? sender, TappedEventArgs e) => OnSet(sender, e);

    private static VisualizerRef ToVisualizerRef(VisualizerOption option) => new()
    {
        Kind = option.Script is not null ? VisualizerRefKind.Scripted
            : option.Installed is not null ? VisualizerRefKind.Installed
            : VisualizerRefKind.Catalog,
        Mode = option.Mode,
        Id = option.Script?.Id ?? option.Installed?.Id,
    };
}
