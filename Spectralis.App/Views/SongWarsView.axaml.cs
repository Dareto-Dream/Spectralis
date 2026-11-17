using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.ViewModels;
using Spectralis.Core.SongWars;

namespace Spectralis.App.Views;

public partial class SongWarsView : UserControl
{
    public Action? PopOutRequested { get; set; }
    public SongWarsSessionController? CurrentSession => (DataContext as SongWarsViewModel)?.Session;
    public SongWarsTournament? CurrentTournament => CurrentSession?.Tournament;
    public SongWarsView() => InitializeComponent();
    private void OnPopOutClick(object? sender, RoutedEventArgs e) => PopOutRequested?.Invoke();
    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SongWarsViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage) return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Audio") { Patterns = ["*.mp3", "*.flac", "*.wav", "*.ogg", "*.m4a", "*.aac"] }]
        });
        foreach (var file in files)
            if (file.TryGetLocalPath() is { } path) vm.AddEntry(System.IO.Path.GetFileNameWithoutExtension(path), "", path);
    }
    private void OnRemoveEntry(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SongWarsViewModel vm && sender is Control { DataContext: SongWarsSubmission entry }) vm.Entries.Remove(entry);
    }
}
