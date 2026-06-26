using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.Core.Metadata;
using Spectralis.Core.Playlists;

namespace Spectralis.App.ViewModels;

/// <summary>One row in the playlist browser: a static playlist or a starred smart playlist.</summary>
public sealed class PlaylistRow
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int TrackCount { get; init; }
    public required bool IsSmart { get; init; }

    public string DisplayName => IsSmart ? $"★ {Name}" : Name;
    public string TypeLabel => IsSmart ? "Smart" : "Playlist";
    public string TrackCountText => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";
}

public sealed class PlaylistsViewModel : ViewModelBase
{
    private readonly LibraryDatabase _database;
    private readonly Func<IReadOnlyList<string>, int, Task> _playQueue;
    private List<Playlist> _playlists = new();
    private List<SmartPlaylist> _smartPlaylists = new();
    private PlaylistRow? _selectedRow;

    public PlaylistsViewModel(LibraryDatabase database, Func<IReadOnlyList<string>, int, Task> playQueue)
    {
        _database = database;
        _playQueue = playQueue;
        Reload();
    }

    public ObservableCollection<PlaylistRow> Rows { get; } = new();

    public PlaylistRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    public bool HasPlaylists => Rows.Count > 0;

    public string StatusText
    {
        get
        {
            var count = _playlists.Count + _smartPlaylists.Count;
            return count == 1 ? "1 playlist" : $"{count} playlists";
        }
    }

    public void Reload()
    {
        _playlists = PlaylistStore.LoadAll();
        _smartPlaylists = PlaylistStore.LoadAllSmart();

        Rows.Clear();
        foreach (var playlist in _playlists)
        {
            Rows.Add(new PlaylistRow
            {
                Id = playlist.Id,
                Name = playlist.Name,
                TrackCount = playlist.Items.Count,
                IsSmart = false,
            });
        }

        var library = _smartPlaylists.Count > 0 ? _database.GetAllEntries() : [];
        foreach (var smart in _smartPlaylists)
        {
            Rows.Add(new PlaylistRow
            {
                Id = smart.Id,
                Name = smart.Name,
                TrackCount = SmartPlaylistEvaluator.Evaluate(smart, library).Count,
                IsSmart = true,
            });
        }

        this.RaisePropertyChanged(nameof(HasPlaylists));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    public Playlist? FindPlaylist(Guid id) => _playlists.FirstOrDefault(p => p.Id == id);

    public SmartPlaylist? FindSmartPlaylist(Guid id) => _smartPlaylists.FirstOrDefault(p => p.Id == id);

    /// <summary>Resolves a row to its playable paths (static items or evaluated smart rules).</summary>
    public IReadOnlyList<string> GetPathsForRow(PlaylistRow row)
    {
        if (row.IsSmart)
        {
            var smart = FindSmartPlaylist(row.Id);
            return smart is null ? [] : SmartPlaylistEvaluator.Evaluate(smart, _database.GetAllEntries());
        }

        var playlist = FindPlaylist(row.Id);
        return playlist is null ? [] : playlist.Items.Select(item => item.Path).ToList();
    }

    public async Task PlayRowAsync(PlaylistRow? row)
    {
        if (row is null)
        {
            return;
        }

        var paths = GetPathsForRow(row).Where(File.Exists).ToList();
        if (paths.Count > 0)
        {
            await _playQueue(paths, 0);
        }
    }

    public Playlist CreatePlaylist(string name, IEnumerable<string> paths)
    {
        var playlist = new Playlist { Name = name };
        playlist.Items.AddRange(BuildItems(paths));
        PlaylistStore.Save(playlist);
        Reload();
        return playlist;
    }

    public SmartPlaylist CreateSmartPlaylist(string name)
    {
        var smart = new SmartPlaylist { Name = name };
        PlaylistStore.SaveSmart(smart);
        Reload();
        return smart;
    }

    public void SavePlaylist(Playlist playlist)
    {
        PlaylistStore.Save(playlist);
        Reload();
    }

    public void SaveSmartPlaylist(SmartPlaylist smart)
    {
        PlaylistStore.SaveSmart(smart);
        Reload();
    }

    public void DeleteRow(PlaylistRow row)
    {
        if (row.IsSmart)
        {
            PlaylistStore.DeleteSmart(row.Id);
        }
        else
        {
            PlaylistStore.Delete(row.Id);
        }

        Reload();
    }

    public Playlist ImportM3u(string filePath)
    {
        var items = M3uParser.ImportItems(filePath);
        var playlist = new Playlist { Name = Path.GetFileNameWithoutExtension(filePath) };
        playlist.Items.AddRange(items);
        PlaylistStore.Save(playlist);
        Reload();
        return playlist;
    }

    public void ExportRow(PlaylistRow row, string filePath)
    {
        if (row.IsSmart)
        {
            M3uParser.ExportPaths(filePath, GetPathsForRow(row));
            return;
        }

        var playlist = FindPlaylist(row.Id);
        if (playlist is not null)
        {
            M3uParser.Export(filePath, playlist.Items);
        }
    }

    /// <summary>Builds playlist items, pulling title/artist/duration from the library when indexed.</summary>
    public List<PlaylistItem> BuildItems(IEnumerable<string> paths)
    {
        var known = _database.GetAllTracks()
            .GroupBy(track => track.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        return paths.Select(path =>
        {
            known.TryGetValue(path, out var track);
            return new PlaylistItem
            {
                Path = path,
                Title = track?.DisplayTitle ?? Path.GetFileNameWithoutExtension(path),
                Artist = track?.Artist,
                DurationSeconds = track?.Duration.TotalSeconds ?? 0,
            };
        }).ToList();
    }
}
