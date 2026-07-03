using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Common;
using Spectralis.Core.Integrations.Spotify;
using Spectralis.Core.Metadata;
using Spectralis.Core.Playlists;

namespace Spectralis.App.ViewModels;

/// <summary>One row in the playlist browser: a static playlist, a starred smart playlist, or a
/// playlist mirrored in from Spotify (<see cref="IsSpotify"/>).</summary>
public sealed class PlaylistRow : ViewModelBase
{
    private Bitmap? _coverBitmap;

    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required int TrackCount { get; init; }
    public required bool IsSmart { get; init; }
    public required bool IsSpotify { get; init; }
    public required bool IsPinned { get; init; }
    public required string RuntimeText { get; init; }

    public string DisplayName => IsSmart ? $"★ {Name}" : Name;
    public string TypeLabel => IsSmart ? "Smart" : IsSpotify ? "Spotify" : "Playlist";
    public string TrackCountText => TrackCount == 1 ? "1 track" : $"{TrackCount} tracks";

    /// <summary>Resolved async by <see cref="PlaylistArtResolver"/> after the row is created — null
    /// until then, and null forever if nothing in the fallback chain resolved (the view falls back
    /// to the placeholder glyph in that case).</summary>
    public Bitmap? CoverBitmap
    {
        get => _coverBitmap;
        set => this.RaiseAndSetIfChanged(ref _coverBitmap, value);
    }
}

public sealed class PlaylistsViewModel : ViewModelBase
{
    private readonly LibraryDatabase _database;
    private readonly Func<IReadOnlyList<string>, int, Task> _playQueue;
    private readonly AppSettings _settings;
    private readonly Action<VisualizerRef?>? _applyDefaultVisualizer;
    private readonly SpotifyService _spotify = new();
    private readonly PlaylistArtResolver _artResolver = new();
    private List<Playlist> _playlists = new();
    private List<SmartPlaylist> _smartPlaylists = new();
    private PlaylistRow? _selectedRow;

    public PlaylistsViewModel(
        LibraryDatabase database,
        Func<IReadOnlyList<string>, int, Task> playQueue,
        AppSettings settings,
        Action<VisualizerRef?>? applyDefaultVisualizer = null)
    {
        _database = database;
        _playQueue = playQueue;
        _settings = settings;
        _applyDefaultVisualizer = applyDefaultVisualizer;
        Reload();
        _ = SyncSpotifyPlaylistsAsync();
    }

    /// <summary>Pinned playlists — render above the bar, manually ordered.</summary>
    public ObservableCollection<PlaylistRow> PinnedRows { get; } = new();

    /// <summary>Everything else — render below the bar, most-recently-played first.</summary>
    public ObservableCollection<PlaylistRow> Rows { get; } = new();

    public PlaylistRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    public bool HasPlaylists => PinnedRows.Count > 0 || Rows.Count > 0;

    public bool HasPinnedRows => PinnedRows.Count > 0;

    public string StatusText
    {
        get
        {
            var count = _playlists.Count + _smartPlaylists.Count;
            return count == 1 ? "1 playlist" : $"{count} playlists";
        }
    }

    /// <summary>Order is pinned (manual, by SortOrder) → bar → everything else, most-recently-played
    /// first. Smart playlists don't participate in pinning or recency (their membership is
    /// rule-evaluated on the fly, not a fixed track list with a stable "played" moment) — they just
    /// render at the end of the unpinned section, same spot they've always rendered in.</summary>
    public void Reload()
    {
        _playlists = PlaylistStore.LoadAll();
        _smartPlaylists = PlaylistStore.LoadAllSmart();

        PinnedRows.Clear();
        Rows.Clear();

        var visible = _playlists.Where(p => !p.IsHidden).ToList();
        var pinned = visible.Where(p => p.IsPinned).OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt);
        var unpinned = visible.Where(p => !p.IsPinned)
            .OrderByDescending(p => p.LastPlayedAt ?? DateTime.MinValue)
            .ThenBy(p => p.CreatedAt);

        foreach (var playlist in pinned)
        {
            var row = BuildRow(playlist);
            PinnedRows.Add(row);
            _ = ResolveCoverAsync(row, playlist);
        }

        foreach (var playlist in unpinned)
        {
            var row = BuildRow(playlist);
            Rows.Add(row);
            _ = ResolveCoverAsync(row, playlist);
        }

        var library = _smartPlaylists.Count > 0 ? _database.GetAllEntries() : [];
        var durationByPath = library.ToDictionary(
            e => e.Track.SourcePath, e => e.Track.Duration.TotalSeconds, StringComparer.OrdinalIgnoreCase);
        foreach (var smart in _smartPlaylists)
        {
            var matches = SmartPlaylistEvaluator.Evaluate(smart, library);
            var totalSeconds = matches.Sum(p => durationByPath.GetValueOrDefault(p));
            Rows.Add(new PlaylistRow
            {
                Id = smart.Id,
                Name = smart.Name,
                TrackCount = matches.Count,
                IsSmart = true,
                IsSpotify = false,
                IsPinned = false,
                RuntimeText = TimeFormat.FormatSeconds(totalSeconds),
            });
        }

        this.RaisePropertyChanged(nameof(HasPlaylists));
        this.RaisePropertyChanged(nameof(HasPinnedRows));
        this.RaisePropertyChanged(nameof(StatusText));
    }

    private static PlaylistRow BuildRow(Playlist playlist) => new()
    {
        Id = playlist.Id,
        Name = playlist.Name,
        TrackCount = playlist.Items.Count,
        IsSmart = false,
        IsSpotify = playlist.SpotifyPlaylistId is not null || playlist.IsLikedSongs,
        IsPinned = playlist.IsPinned,
        RuntimeText = TimeFormat.FormatSeconds(playlist.Items.Sum(i => i.DurationSeconds)),
    };

    private async Task ResolveCoverAsync(PlaylistRow row, Playlist playlist)
    {
        var bitmap = await _artResolver.ResolveAsync(playlist);
        row.CoverBitmap = bitmap;
    }

    /// <summary>Pulls the user's Spotify playlists in and caches them the same way local playlists
    /// are stored, so covers/hide/rename/order/default-visualizer survive restarts. Playlist names
    /// are only set on first import — an existing local copy's name is never overwritten by a sync,
    /// which is what makes the "local rename" customization stick.</summary>
    public async Task SyncSpotifyPlaylistsAsync()
    {
        if (!_settings.ImportSpotifyPlaylists || !_spotify.IsLinked || !_spotify.HasPlaylistScopes)
        {
            return;
        }

        var clientId = SpotifyClientIdProvider.ResolveClientId(_settings.SpotifyCustomClientId);
        var remote = await _spotify.GetPlaylistsAsync(clientId);

        if (remote.Count > 0)
        {
            var cached = PlaylistStore.LoadAll();
            var byRemoteId = cached
                .Where(p => p.SpotifyPlaylistId is not null)
                .ToDictionary(p => p.SpotifyPlaylistId!, p => p);

            foreach (var summary in remote)
            {
                var isNew = !byRemoteId.TryGetValue(summary.Id, out var playlist);
                playlist ??= new Playlist { Name = summary.Name, SpotifyPlaylistId = summary.Id };
                playlist.SpotifyImageUrl = summary.ImageUrl;

                if (isNew || playlist.SpotifySnapshotId != summary.SnapshotId)
                {
                    var tracks = await _spotify.GetPlaylistTracksAsync(clientId, summary.Id);
                    playlist.Items = tracks.Select(t => new PlaylistItem
                    {
                        SpotifyTrackUri = t.Uri,
                        Title = t.Name,
                        Artist = t.Artist,
                        DurationSeconds = t.DurationMs / 1000.0,
                        AlbumArtUrl = t.AlbumArtUrl,
                    }).ToList();
                    playlist.SpotifySnapshotId = summary.SnapshotId;
                }

                PlaylistStore.Save(playlist);
                byRemoteId.Remove(summary.Id);
            }

            // Anything left in byRemoteId is a previously-synced playlist Spotify no longer reports
            // (unfollowed/deleted) — drop the stale local cache entry.
            foreach (var stale in byRemoteId.Values)
            {
                PlaylistStore.Delete(stale.Id);
            }
        }

        await SyncLikedSongsAsync(clientId);
        Reload();
    }

    /// <summary>Liked Songs isn't a real playlist (no id, no snapshot) so it's matched by the
    /// IsLikedSongs flag instead of SpotifyPlaylistId, and — unlike normal playlists — always
    /// re-fetched in full since /me/tracks has nothing like a snapshot id to diff against.
    /// Pinned by default, but only on first import, so un-pinning it later sticks across syncs.</summary>
    private async Task SyncLikedSongsAsync(string clientId)
    {
        var tracks = await _spotify.GetLikedSongsAsync(clientId);
        if (tracks.Count == 0)
        {
            return;
        }

        var existing = PlaylistStore.LoadAll().FirstOrDefault(p => p.IsLikedSongs);
        var playlist = existing ?? new Playlist { Name = "Liked Songs", IsLikedSongs = true, IsPinned = true };
        playlist.Items = tracks.Select(t => new PlaylistItem
        {
            SpotifyTrackUri = t.Uri,
            Title = t.Name,
            Artist = t.Artist,
            DurationSeconds = t.DurationMs / 1000.0,
            AlbumArtUrl = t.AlbumArtUrl,
        }).ToList();

        PlaylistStore.Save(playlist);
    }

    public Playlist? FindPlaylist(Guid id) => _playlists.FirstOrDefault(p => p.Id == id);

    public SmartPlaylist? FindSmartPlaylist(Guid id) => _smartPlaylists.FirstOrDefault(p => p.Id == id);

    private static string PlayableRef(PlaylistItem item) => item.SpotifyTrackUri ?? item.Path;

    /// <summary>Resolves a row to its playable paths/uris (static items, evaluated smart rules, or
    /// a mix of local paths and "spotify:track:..." uris for a Spotify-backed/mixed playlist).</summary>
    public IReadOnlyList<string> GetPathsForRow(PlaylistRow row)
    {
        if (row.IsSmart)
        {
            var smart = FindSmartPlaylist(row.Id);
            return smart is null ? [] : SmartPlaylistEvaluator.Evaluate(smart, _database.GetAllEntries());
        }

        var playlist = FindPlaylist(row.Id);
        return playlist is null ? [] : playlist.Items.Select(PlayableRef).ToList();
    }

    public async Task PlayRowAsync(PlaylistRow? row)
    {
        if (row is null)
        {
            return;
        }

        var refs = GetPathsForRow(row)
            .Where(r => r.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase) || File.Exists(r))
            .ToList();
        if (refs.Count == 0)
        {
            return;
        }

        if (!row.IsSmart && FindPlaylist(row.Id) is { } playlist)
        {
            _applyDefaultVisualizer?.Invoke(playlist.DefaultVisualizer);

            // Drives the below-the-bar sort — playing a playlist bumps it to the top of the
            // most-recently-played order. Pinned playlists ignore this (their spot is manual).
            if (!playlist.IsPinned)
            {
                playlist.LastPlayedAt = DateTime.UtcNow;
                PlaylistStore.Save(playlist);
                Reload();
            }
        }

        await _playQueue(refs, 0);
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

    /// <summary>Persists an edited playlist. For a Spotify-backed one, pushes the edited Spotify
    /// items back as a single "replace" call first (covers arbitrary add/remove/reorder in one
    /// request) — any local-file items mixed into the same playlist obviously can't be represented
    /// on Spotify's side and are just skipped for that call, but stay in the local copy.</summary>
    public async Task SavePlaylist(Playlist playlist)
    {
        if (playlist.SpotifyPlaylistId is not null)
        {
            var clientId = SpotifyClientIdProvider.ResolveClientId(_settings.SpotifyCustomClientId);
            var spotifyUris = playlist.Items
                .Where(i => i.SpotifyTrackUri is not null)
                .Select(i => i.SpotifyTrackUri!)
                .ToList();
            var newSnapshot = await _spotify.ReplacePlaylistItemsAsync(clientId, playlist.SpotifyPlaylistId, spotifyUris);
            if (newSnapshot is not null)
            {
                playlist.SpotifySnapshotId = newSnapshot;
            }
        }

        PlaylistStore.Save(playlist);
        Reload();
    }

    public void SaveSmartPlaylist(SmartPlaylist smart)
    {
        PlaylistStore.SaveSmart(smart);
        Reload();
    }

    /// <summary>For a Spotify-backed playlist this just hides it from the grid — it's still the
    /// user's real Spotify playlist, Spectralis has no business deleting it. A future sync will
    /// bring it right back unless the user also un-follows it on Spotify's side.</summary>
    public void DeleteRow(PlaylistRow row)
    {
        if (row.IsSmart)
        {
            PlaylistStore.DeleteSmart(row.Id);
        }
        else if (FindPlaylist(row.Id) is { SpotifyPlaylistId: not null } spotifyBacked)
        {
            spotifyBacked.IsHidden = true;
            PlaylistStore.Save(spotifyBacked);
        }
        else
        {
            PlaylistStore.Delete(row.Id);
        }

        Reload();
    }

    /// <summary>Pinned playlists render above the bar and stop sorting by recency; a freshly
    /// pinned one lands at the end of the pinned section until manually moved.</summary>
    public void TogglePinned(PlaylistRow row)
    {
        if (row.IsSmart || FindPlaylist(row.Id) is not { } playlist)
        {
            return;
        }

        if (!playlist.IsPinned)
        {
            playlist.SortOrder = _playlists.Where(p => p.IsPinned).Select(p => p.SortOrder).DefaultIfEmpty(-1).Max() + 1;
        }

        playlist.IsPinned = !playlist.IsPinned;
        PlaylistStore.Save(playlist);
        Reload();
    }

    public void SetHidden(PlaylistRow row, bool hidden)
    {
        if (row.IsSmart || FindPlaylist(row.Id) is not { } playlist)
        {
            return;
        }

        playlist.IsHidden = hidden;
        PlaylistStore.Save(playlist);
        Reload();
    }

    /// <summary>Renames the local copy only — never touches the real Spotify playlist's name.</summary>
    public void RenameLocally(PlaylistRow row, string newName)
    {
        if (row.IsSmart || string.IsNullOrWhiteSpace(newName) || FindPlaylist(row.Id) is not { } playlist)
        {
            return;
        }

        playlist.Name = newName.Trim();
        PlaylistStore.Save(playlist);
        Reload();
    }

    /// <summary>Copies the chosen image into app data (so it survives the source file moving) and
    /// sets it as the playlist's cover — highest priority in the art fallback chain. Pass null to
    /// clear the override and fall back to the next tier.</summary>
    public void SetCoverImage(PlaylistRow row, string? sourceImagePath)
    {
        if (row.IsSmart || FindPlaylist(row.Id) is not { } playlist)
        {
            return;
        }

        if (!string.IsNullOrEmpty(playlist.CoverImagePath))
        {
            try { File.Delete(playlist.CoverImagePath); } catch { /* best effort cleanup */ }
        }

        if (sourceImagePath is null)
        {
            playlist.CoverImagePath = null;
        }
        else
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis", "playlist-covers");
            Directory.CreateDirectory(dir);
            var destPath = Path.Combine(dir, $"{playlist.Id:N}{Path.GetExtension(sourceImagePath)}");
            File.Copy(sourceImagePath, destPath, overwrite: true);
            playlist.CoverImagePath = destPath;
        }

        PlaylistStore.Save(playlist);
        Reload();
    }

    /// <summary>Manual reorder within the pinned section. direction is -1 (up/earlier) or +1
    /// (down/later). Unpinned rows sort by recency instead, so this is a no-op for them.</summary>
    public void MoveRow(PlaylistRow row, int direction)
    {
        if (row.IsSmart || !row.IsPinned)
        {
            return;
        }

        var ordered = _playlists.Where(p => !p.IsHidden && p.IsPinned).OrderBy(p => p.SortOrder).ThenBy(p => p.CreatedAt).ToList();
        var index = ordered.FindIndex(p => p.Id == row.Id);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= ordered.Count)
        {
            return;
        }

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (var i = 0; i < ordered.Count; i++)
        {
            ordered[i].SortOrder = i;
            PlaylistStore.Save(ordered[i]);
        }

        Reload();
    }

    public void SetDefaultVisualizer(PlaylistRow row, VisualizerRef? visualizerRef)
    {
        if (row.IsSmart || FindPlaylist(row.Id) is not { } playlist)
        {
            return;
        }

        playlist.DefaultVisualizer = visualizerRef;
        PlaylistStore.Save(playlist);
        Reload();
    }

    /// <summary>Adds a Spotify search result (see Library search) into an existing local or
    /// Spotify-backed playlist — the mixed-playlist path for Library-search-found tracks.</summary>
    public async Task AddSpotifyTrackAsync(Guid playlistId, SpotifyTrackResult track)
    {
        if (FindPlaylist(playlistId) is not { } playlist)
        {
            return;
        }

        var item = new PlaylistItem
        {
            SpotifyTrackUri = track.Uri,
            Title = track.Name,
            Artist = track.Artist,
            DurationSeconds = track.DurationMs / 1000.0,
            AlbumArtUrl = track.AlbumArtUrl,
        };

        if (playlist.SpotifyPlaylistId is not null)
        {
            var clientId = SpotifyClientIdProvider.ResolveClientId(_settings.SpotifyCustomClientId);
            var newSnapshot = await _spotify.AddPlaylistItemsAsync(clientId, playlist.SpotifyPlaylistId, [track.Uri]);
            if (newSnapshot is null)
            {
                return;
            }

            playlist.SpotifySnapshotId = newSnapshot;
        }

        playlist.Items.Add(item);
        PlaylistStore.Save(playlist);
        Reload();
    }

    /// <summary>Adds a local library track into an existing playlist — the local-file counterpart
    /// to <see cref="AddSpotifyTrackAsync"/>, used by the same Library "Add to Playlist" action.</summary>
    public void AddLocalTrack(Guid playlistId, string path)
    {
        if (FindPlaylist(playlistId) is not { } playlist)
        {
            return;
        }

        playlist.Items.AddRange(BuildItems([path]));
        PlaylistStore.Save(playlist);
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
