using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.ViewModels;
using Spectralis.Core.Common;
using Spectralis.App.Services;

namespace Spectralis.App.Views;

public partial class DjDeckView : UserControl
{
    public DjDeckView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, (_, e) => { e.DragEffects = DataContext is DjDeckViewModel { CanLoad: true } ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; });
        AddHandler(DragDrop.DropEvent, async (_, e) =>
        {
            e.Handled = true;
            var paths = e.Data.GetFiles()?.Select(f => f.TryGetLocalPath()).Where(p => p is not null && SupportedAudioFormats.IsSupportedExtension(p)).Cast<string>().ToArray();
            if (paths is { Length: > 0 } && DataContext is DjDeckViewModel vm)
                await vm.LoadFilesAsync(paths);
            else if (DataContext is DjDeckViewModel urlDeck && e.Data.GetText() is { } text && OpenUrlService.MightBeUrl(text))
                await urlDeck.LoadFilesAsync([text.Trim()]);
        });
    }
    internal static FilePickerFileType AudioType => new("Audio") { Patterns = SupportedAudioFormats.Extensions.Select(e => "*" + e).ToArray() };
    private async void OnLoadUrl(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DjDeckViewModel { CanLoad: true } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        vm.IsSelected = true;
        var url = await OpenUrlWindow.PromptAsync(top, vm.Label);
        if (!string.IsNullOrWhiteSpace(url)) await vm.LoadFilesAsync([url]);
    }
    private void OnCancelLoad(object? sender, RoutedEventArgs e) => (DataContext as DjDeckViewModel)?.CancelLoad();
    private async void OnLoadFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DjDeckViewModel { CanLoad: true } vm || TopLevel.GetTopLevel(this) is not { } top) return;
        vm.IsSelected = true;
        var files = await top.StorageProvider.OpenFilePickerAsync(new()
        {
            Title = "Load audio or aligned stems", AllowMultiple = true, FileTypeFilter = [AudioType],
        });
        var paths = files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray();
        await vm.LoadFilesAsync(paths);
    }
}
