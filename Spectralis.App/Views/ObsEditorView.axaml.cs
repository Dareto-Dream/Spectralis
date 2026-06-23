using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Spectralis.App.ViewModels;
using Spectralis.Core.Integrations.Obs;

namespace Spectralis.App.Views;

public partial class ObsEditorView : UserControl
{
    private ObsDesignerItem? _selectedItem;
    private bool _syncingSelection;
    private bool _updatingProps;

    public ObsEditorView()
    {
        InitializeComponent();
        Designer.SelectedItemChanged += OnDesignerSelectedItemChanged;
        Designer.StatusChanged += OnDesignerStatusChanged;
        AutoApplyToggle.IsCheckedChanged += (_, _) => Designer.AutoApply = AutoApplyToggle.IsChecked == true;
    }

    private ObsEditorViewModel? Vm => DataContext as ObsEditorViewModel;

    // ─── Designer events ──────────────────────────────────────────────────────
    private void OnDesignerSelectedItemChanged(object? sender, ObsDesignerItem? item)
    {
        _selectedItem = item;

        if (!_syncingSelection)
        {
            _syncingSelection = true;
            WidgetListBox.SelectedItem = item;
            _syncingSelection = false;
        }

        UpdatePropertiesPanel(item);
        RemoveWidgetBtn.IsEnabled = item is not null;
    }

    private void OnDesignerStatusChanged(object? sender, string? status)
    {
        CanvasStatusBar.IsVisible = status is not null;
        CanvasStatusText.Text = status ?? string.Empty;
    }

    // ─── Widget list sync ────────────────────────────────────────────────────
    private void OnWidgetListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        Designer.SelectItem(WidgetListBox.SelectedItem as ObsDesignerItem);
        _syncingSelection = false;
    }

    // ─── Properties panel ────────────────────────────────────────────────────
    private void UpdatePropertiesPanel(ObsDesignerItem? item)
    {
        PropsShared.IsVisible    = item is not null;
        PropsNowPlaying.IsVisible = item?.Source.Type == ObsWidgetType.NowPlaying;
        PropsVisualizer.IsVisible = item?.Source.Type == ObsWidgetType.Visualizer;

        if (item is null) return;

        _updatingProps = true;

        BgOpacitySlider.Value = item.Source.BgOpacity;
        BgOpacityLabel.Text   = $"{item.Source.BgOpacity}%";

        if (item.Source.Type == ObsWidgetType.NowPlaying)
        {
            ShowArtCheck.IsChecked     = item.Source.ShowArt;
            ShowArtistCheck.IsChecked  = item.Source.ShowArtist;
            ShowProgressCheck.IsChecked = item.Source.ShowProgress;
            ArtSquareRadio.IsChecked   = item.Source.ArtShape == "square";
            ArtCircleRadio.IsChecked   = item.Source.ArtShape != "square";
        }

        if (item.Source.Type == ObsWidgetType.Visualizer)
        {
            VizKeyBox.Text = item.Source.VizKey ?? string.Empty;
        }

        _updatingProps = false;
    }

    private void TryAutoApply()
    {
        if (!Designer.AutoApply) return;
        var layout = Designer.CollectLayout();
        Vm?.ApplyDesignerLayout(layout);
    }

    // ─── Apply / Remove ──────────────────────────────────────────────────────
    private void OnApply(object? sender, RoutedEventArgs e)
    {
        var layout = Designer.CollectLayout();
        Vm?.ApplyDesignerLayout(layout);
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e) =>
        Designer.RemoveSelectedWidget();

    // ─── Widget palette ──────────────────────────────────────────────────────
    private void OnAddNowPlaying(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.NowPlaying);

    private void OnAddLyrics(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.Lyrics);

    private void OnAddQueue(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.Queue);

    private void OnAddProgress(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.Progress);

    private void OnAddVisualizer(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.Visualizer);

    private void OnAddSongWars(object? sender, RoutedEventArgs e) =>
        Designer.AddWidget(ObsWidgetType.SongWarsBracket);

    // ─── Overlay management ──────────────────────────────────────────────────
    private async void OnCopySelectedOverlayUrl(object? sender, RoutedEventArgs e)
    {
        var url = Vm?.SelectedOverlayUrl;
        if (!string.IsNullOrEmpty(url) && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await cb.SetTextAsync(url);
    }

    private async void OnCopyObsToken(object? sender, RoutedEventArgs e)
    {
        var token = Vm?.ObsToken;
        if (!string.IsNullOrEmpty(token) && TopLevel.GetTopLevel(this)?.Clipboard is { } cb)
            await cb.SetTextAsync(token);
    }

    private void OnRegenerateObsToken(object? sender, RoutedEventArgs e) =>
        Vm?.RegenerateObsToken();

    private void OnAddOverlay(object? sender, RoutedEventArgs e) =>
        Vm?.BeginAddOverlay();

    private void OnConfirmAddOverlay(object? sender, RoutedEventArgs e) =>
        Vm?.ConfirmAddOverlay();

    private void OnCancelAddOverlay(object? sender, RoutedEventArgs e) =>
        Vm?.CancelAddOverlay();

    private void OnRemoveOverlay(object? sender, RoutedEventArgs e) =>
        Vm?.RemoveSelectedOverlay();

    // ─── Presets ─────────────────────────────────────────────────────────────
    private void OnApplyBuiltInPresetChip(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ObsPreset preset)
            Vm?.ApplyPresetLayout(preset);
    }

    private void OnApplyUserPresetChip(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ObsPreset preset)
            Vm?.ApplyPresetLayout(preset);
    }

    private void OnDeleteUserPresetChip(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.Tag is ObsPreset preset)
            Vm?.DeletePreset(preset);
    }

    private void OnSavePreset(object? sender, RoutedEventArgs e) =>
        Vm?.SaveCurrentAsPreset();

    // ─── Shared props ─────────────────────────────────────────────────────────
    private void OnBgOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        var val = (int)e.NewValue;
        _selectedItem.Source.BgOpacity = val;
        BgOpacityLabel.Text = $"{val}%";
        TryAutoApply();
    }

    // ─── Now Playing props ────────────────────────────────────────────────────
    private void OnShowArtChanged(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        _selectedItem.Source.ShowArt = ShowArtCheck.IsChecked == true;
        TryAutoApply();
    }

    private void OnShowArtistChanged(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        _selectedItem.Source.ShowArtist = ShowArtistCheck.IsChecked == true;
        TryAutoApply();
    }

    private void OnShowProgressChanged(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        _selectedItem.Source.ShowProgress = ShowProgressCheck.IsChecked == true;
        TryAutoApply();
    }

    private void OnArtShapeChanged(object? sender, RoutedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        _selectedItem.Source.ArtShape = ArtSquareRadio.IsChecked == true ? "square" : "circle";
        TryAutoApply();
    }

    // ─── Visualizer props ────────────────────────────────────────────────────
    private void OnVizKeyChanged(object? sender, TextChangedEventArgs e)
    {
        if (_selectedItem is null || _updatingProps) return;
        _selectedItem.Source.VizKey = VizKeyBox.Text ?? string.Empty;
        TryAutoApply();
    }
}
