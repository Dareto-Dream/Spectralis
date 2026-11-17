using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Controls;
using Spectralis.App.ViewModels;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Views;

public partial class VisualizerPickerWindow : Window
{
    private bool _confirmed;
    private VisualizerOption? _selection;
    public VisualizerPickerWindow()
    {
        InitializeComponent();
    }

    public static async Task<VisualizerOption?> ChooseAsync(Window owner, IEnumerable<VisualizerOption> options,
        VisualizerOption selected, NowPlayingViewModel? player = null, string action = "Use visualizer")
    {
        var window = new VisualizerPickerWindow { DataContext = new VisualizerGalleryViewModel(options, selected, player?.CoverArtBytes) };
        window.ClearButton.IsVisible = false;
        window.ApplyButton.Content = action;
        await window.ShowDialog(owner);
        return window._confirmed ? window._selection : null;
    }

    public static async Task<(bool Confirmed, VisualizerRef? Result)> PromptAsync(Window owner, IEnumerable<VisualizerOption> options)
    {
        var window = new VisualizerPickerWindow { DataContext = new VisualizerGalleryViewModel(options) };
        window.ApplyButton.Content = "Set playlist default";
        await window.ShowDialog(owner);
        return (window._confirmed, window._selection is { } option ? ToVisualizerRef(option) : null);
    }

    private void OnSet(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VisualizerGalleryViewModel { SelectedVariant: { } option, IsEmpty: false }) return;
        _selection = option;
        _confirmed = true;
        Close();
    }
    private void OnClear(object? sender, RoutedEventArgs e) { _selection = null; _confirmed = true; Close(); }
    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
    private static VisualizerRef ToVisualizerRef(VisualizerOption option) => new()
    {
        Kind = option.Script is not null ? VisualizerRefKind.Scripted : option.Installed is not null ? VisualizerRefKind.Installed : VisualizerRefKind.Catalog,
        Mode = option.Mode, Id = option.Script?.Id ?? option.Installed?.Id,
    };
}
