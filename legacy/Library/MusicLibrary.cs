namespace Spectralis;

internal sealed class MusicLibrary
{
    private readonly object _lock = new();
    private readonly List<LibraryTrack> _tracks = [];
    private LibraryStore? _store;

    public event EventHandler? TracksChanged;

    public IReadOnlyList<LibraryTrack> Tracks
    {
        get { lock (_lock) return _tracks.ToList(); }
    }

    public void Initialize(LibraryStore store)
    {
        _store = store;
        var all = store.GetAll();
        lock (_lock)
        {
            _tracks.Clear();
            _tracks.AddRange(all);
        }
    }

    public void Upsert(LibraryTrack track)
    {
        lock (_lock)
        {
            var idx = _tracks.FindIndex(t =>
                string.Equals(t.Path, track.Path, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
                _tracks[idx] = track;
            else
                _tracks.Add(track);
        }
        _store?.Upsert(track);
        TracksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Remove(string path)
    {
        lock (_lock)
            _tracks.RemoveAll(t =>
                string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        _store?.Delete(path);
        TracksChanged?.Invoke(this, EventArgs.Empty);
    }

    public LibraryTrack? Find(string path)
    {
        lock (_lock)
            return _tracks.FirstOrDefault(t =>
                string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
    }

    public void UpsertAnalysis(LibraryTrack updated)
    {
        lock (_lock)
        {
            var idx = _tracks.FindIndex(t =>
                string.Equals(t.Path, updated.Path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            _tracks[idx] = updated;
        }
        TracksChanged?.Invoke(this, EventArgs.Empty);
    }

    public void IncrementPlayCount(string path)
    {
        LibraryTrack? updated = null;
        lock (_lock)
        {
            var idx = _tracks.FindIndex(t =>
                string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return;
            var t = _tracks[idx];
            updated = t with { PlayCount = t.PlayCount + 1, LastPlayed = DateTime.UtcNow };
            _tracks[idx] = updated;
        }
        if (updated is not null)
        {
            _store?.Upsert(updated);
            TracksChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public List<LibraryTrack> Search(string query, string filter)
    {
        List<LibraryTrack> source;
        lock (_lock) source = _tracks.ToList();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var q = query.Trim();
            source = source.Where(t =>
                t.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Artist.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                t.Album.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        return filter switch
        {
            "Artists" => source.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.Title).ToList(),
            "Albums"  => source.OrderBy(t => t.Album).ThenBy(t => t.Title).ToList(),
            "Genres"  => source.OrderBy(t => t.Genre).ThenBy(t => t.Artist).ThenBy(t => t.Title).ToList(),
            _         => source.OrderBy(t => t.Artist).ThenBy(t => t.Album).ThenBy(t => t.Title).ToList()
        };
    }
}
