using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Playlists;

public sealed class Playlist
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Playlist";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<PlaylistItem> Items { get; set; } = [];

    /// <summary>Spotify's playlist id. Non-null means this playlist is backed by a live
    /// Spotify playlist and track add/remove/reorder write back to Spotify.</summary>
    public string? SpotifyPlaylistId { get; set; }

    /// <summary>Spotify's optimistic-concurrency token for this playlist's track list.
    /// Required on every write so a stale local copy can't clobber a concurrent edit.</summary>
    public string? SpotifySnapshotId { get; set; }

    /// <summary>User-uploaded cover, highest priority in the art fallback chain. A path into
    /// app data, copied there at pick-time so it survives the source file moving/deleting.</summary>
    public string? CoverImagePath { get; set; }

    /// <summary>Spotify's own cover image URL, cached from the last playlist sync — second
    /// priority in the art fallback chain, below <see cref="CoverImagePath"/>.</summary>
    public string? SpotifyImageUrl { get; set; }

    /// <summary>Hidden from the Playlists grid without affecting the real Spotify playlist.</summary>
    public bool IsHidden { get; set; }

    /// <summary>Manual position among pinned playlists; lower sorts first. Unpinned playlists
    /// ignore this — their order is driven by <see cref="LastPlayedAt"/> instead.</summary>
    public int SortOrder { get; set; }

    /// <summary>Applied via NowPlayingViewModel.SelectedVisualizer when this playlist starts playing.</summary>
    public VisualizerRef? DefaultVisualizer { get; set; }

    /// <summary>Pinned playlists render above the bar in the Playlists grid, manually ordered by
    /// <see cref="SortOrder"/>. Everything else renders below it, most-recently-played first.</summary>
    public bool IsPinned { get; set; }

    /// <summary>Set whenever this playlist is played; drives the below-the-bar sort order.</summary>
    public DateTime? LastPlayedAt { get; set; }

    /// <summary>True for the single synthetic playlist mirroring Spotify's Liked Songs (via
    /// /me/tracks, not a real playlist id) — pinned by default on first import. Distinct from
    /// <see cref="SpotifyPlaylistId"/>, which stays null here since Liked Songs isn't addressable
    /// through the normal /playlists/{id} write endpoints.</summary>
    public bool IsLikedSongs { get; set; }
}

public sealed class PlaylistItem
{
    /// <summary>Local file path. Empty/null for a Spotify-sourced item — see <see cref="SpotifyTrackUri"/>.</summary>
    public string Path { get; set; } = "";

    /// <summary>Spotify track URI ("spotify:track:..."), set instead of <see cref="Path"/> for a
    /// Spotify-sourced item so one playlist can mix local files and Spotify tracks.</summary>
    public string? SpotifyTrackUri { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public double DurationSeconds { get; set; }

    /// <summary>Cached per-track Spotify art URL for <see cref="SpotifyTrackUri"/> items — local
    /// items get their art read straight from embedded tags instead, so this stays null for those.</summary>
    public string? AlbumArtUrl { get; set; }
}

/// <summary>A persistable pointer to one visualizer, spanning the three ways one can be identified
/// (built-in catalog by enum, or a scripted/installed visualizer by string id) — nothing like this
/// existed before since visualizer selection was previously session-only for non-catalog entries.</summary>
public sealed class VisualizerRef
{
    public VisualizerRefKind Kind { get; set; }
    public VisualizerMode? Mode { get; set; }
    public string? Id { get; set; }
}

public enum VisualizerRefKind { Catalog, Scripted, Installed }

public sealed class SmartPlaylist
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "New Smart Playlist";
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<SmartRule> Rules { get; set; } = [];
    public SmartMatchMode Match { get; set; } = SmartMatchMode.All;
    public int Limit { get; set; }  // 0 = unlimited
    public SmartSortField SortBy { get; set; } = SmartSortField.DateAdded;
    public bool SortDescending { get; set; } = true;
}

public sealed class SmartRule
{
    public SmartRuleField Field { get; set; } = SmartRuleField.Title;
    public SmartRuleOp Op { get; set; } = SmartRuleOp.Contains;
    public string Value { get; set; } = "";
}

public enum SmartMatchMode { All, Any }
public enum SmartSortField { Title, Artist, Album, Year, PlayCount, LastPlayed, DateAdded, Duration }
public enum SmartRuleField { Title, Artist, Album, AlbumArtist, Genre, Year, PlayCount, Duration }
public enum SmartRuleOp { Contains, NotContains, Is, IsNot, GreaterThan, LessThan }
