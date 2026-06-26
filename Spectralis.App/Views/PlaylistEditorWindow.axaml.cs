using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.Core.Common;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Views;

/// <summary>Static playlist editor: rename, add/remove files, reorder.</summary>
public partial class PlaylistEditorWindow : Window
{
    private readonly List<PlaylistItem> _items = new();
    private Playlist? _playlist;

    public bool Saved { get; private set; }

    public PlaylistEditorWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> EditAsync(Window owner, Playlist playlist, Func<IEnumerable<string>, List<PlaylistItem>> buildItems)
    {
        var window = new PlaylistEditorWindow
        {
            _playlist = playlist,
        };
        window._buildItems = buildItems;
        window.NameBox.Text = playlist.Name;
        window._items.AddRange(playlist.Items);
        window.RefreshList();
        await window.ShowDialog(owner);
        return window.Saved;
    }

    private Func<IEnumerable<string>, List<PlaylistItem>>? _buildItems;

    private void RefreshList()
    {
        ItemsList.ItemsSource = _items
            .Select(item => string.IsNullOrWhiteSpace(item.Artist)
                ? item.Title ?? Path.GetFileNameWithoutExtension(item.Path)
                : $"{item.Artist} - {item.Title}")
            .ToList();
        CountText.Text = _items.Count == 1 ? "1 track" : $"{_items.Count} tracks";
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add files to playlist",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("Audio files")
                {
                    Patterns = SupportedAudioFormats.Extensions.Select(ext => "*" + ext).ToArray(),
                },
                FilePickerFileTypes.All,
            ],
        });

        var paths = files
            .Select(file => file.TryGetLocalPath())
            .Where(path => path is not null && SupportedAudioFormats.IsSupportedExtension(path))
            .Select(path => path!)
            .ToList();

        if (paths.Count > 0 && _buildItems is not null)
        {
            _items.AddRange(_buildItems(paths));
            RefreshList();
        }
    }

    private void OnRemoveSelected(object? sender, RoutedEventArgs e)
    {
        var indices = ItemsList.Selection.SelectedIndexes.OrderByDescending(i => i).ToList();
        foreach (var index in indices)
        {
            if (index >= 0 && index < _items.Count)
            {
                _items.RemoveAt(index);
            }
        }

        RefreshList();
    }

    private void OnMoveUp(object? sender, RoutedEventArgs e)
    {
        var index = ItemsList.SelectedIndex;
        if (index <= 0 || index >= _items.Count)
        {
            return;
        }

        (_items[index], _items[index - 1]) = (_items[index - 1], _items[index]);
        RefreshList();
        ItemsList.SelectedIndex = index - 1;
    }

    private void OnMoveDown(object? sender, RoutedEventArgs e)
    {
        var index = ItemsList.SelectedIndex;
        if (index < 0 || index >= _items.Count - 1)
        {
            return;
        }

        (_items[index], _items[index + 1]) = (_items[index + 1], _items[index]);
        RefreshList();
        ItemsList.SelectedIndex = index + 1;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_playlist is null)
        {
            Close();
            return;
        }

        var name = NameBox.Text?.Trim();
        if (!string.IsNullOrEmpty(name))
        {
            _playlist.Name = name;
        }

        _playlist.Items = _items.ToList();
        Saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
