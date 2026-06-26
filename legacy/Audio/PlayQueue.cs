namespace Spectralis;

public enum RepeatMode { None, All, One }

public sealed class PlayQueue
{
    private readonly List<string> _items = [];
    private readonly Random _random = new();
    private List<int>? _shuffleOrder;
    private int _shufflePos = -1;
    private int _currentIndex = -1;
    private bool _shuffle;

    public IReadOnlyList<string> Items => _items;
    public int CurrentIndex => _currentIndex;
    public int Count => _items.Count;
    public bool IsEmpty => _items.Count == 0;
    public RepeatMode Repeat { get; set; } = RepeatMode.None;

    public bool Shuffle
    {
        get => _shuffle;
        set
        {
            _shuffle = value;
            if (value) RebuildShuffle();
            else { _shuffleOrder = null; _shufflePos = -1; }
        }
    }

    public string? CurrentPath =>
        _currentIndex >= 0 && _currentIndex < _items.Count ? _items[_currentIndex] : null;

    public bool HasNext => NextIndex() >= 0;
    public bool HasPrevious => PreviousIndex() >= 0;

    public void Add(string path) { _items.Add(path); if (_shuffle) RebuildShuffle(); }

    public void AddRange(IEnumerable<string> paths)
    {
        _items.AddRange(paths);
        if (_shuffle) RebuildShuffle();
    }

    public void InsertRange(int index, IEnumerable<string> paths)
    {
        var insertItems = paths.ToArray();
        if (insertItems.Length == 0) return;

        var insertAt = Math.Clamp(index, 0, _items.Count);
        _items.InsertRange(insertAt, insertItems);
        if (_currentIndex >= insertAt)
            _currentIndex += insertItems.Length;
        if (_shuffle) RebuildShuffle();
    }

    public void Remove(int index)
    {
        if (index < 0 || index >= _items.Count) return;
        _items.RemoveAt(index);
        if (index < _currentIndex) _currentIndex--;
        else if (index == _currentIndex) _currentIndex = Math.Min(_currentIndex, _items.Count - 1);
        if (_shuffle) RebuildShuffle();
    }

    public void MoveUp(int index)
    {
        if (index <= 0 || index >= _items.Count) return;
        (_items[index], _items[index - 1]) = (_items[index - 1], _items[index]);
        if (_currentIndex == index) _currentIndex--;
        else if (_currentIndex == index - 1) _currentIndex++;
        if (_shuffle) RebuildShuffle();
    }

    public void MoveDown(int index)
    {
        if (index < 0 || index >= _items.Count - 1) return;
        (_items[index], _items[index + 1]) = (_items[index + 1], _items[index]);
        if (_currentIndex == index) _currentIndex++;
        else if (_currentIndex == index + 1) _currentIndex--;
        if (_shuffle) RebuildShuffle();
    }

    public void Clear()
    {
        _items.Clear();
        _currentIndex = -1;
        _shuffleOrder = null;
        _shufflePos = -1;
    }

    public string? SetCurrent(int index)
    {
        if (index < 0 || index >= _items.Count) return null;
        _currentIndex = index;
        if (_shuffle && _shuffleOrder is not null)
            _shufflePos = _shuffleOrder.IndexOf(index);
        return _items[_currentIndex];
    }

    public string? MoveNext()
    {
        var next = NextIndex();
        return next >= 0 ? SetCurrent(next) : null;
    }

    public string? MovePrevious()
    {
        var prev = PreviousIndex();
        return prev >= 0 ? SetCurrent(prev) : null;
    }

    private int NextIndex()
    {
        if (_items.Count == 0) return -1;
        if (Repeat == RepeatMode.One && _currentIndex >= 0) return _currentIndex;

        if (_shuffle)
        {
            if (_shuffleOrder is null || _shuffleOrder.Count == 0) return -1;
            var nextPos = _shufflePos + 1;
            if (nextPos >= _shuffleOrder.Count)
            {
                if (Repeat == RepeatMode.All) { RebuildShuffle(); return _shuffleOrder![0]; }
                return -1;
            }
            return _shuffleOrder[nextPos];
        }

        var next = _currentIndex + 1;
        if (next >= _items.Count)
            return Repeat == RepeatMode.All ? 0 : -1;
        return next;
    }

    private int PreviousIndex()
    {
        if (_items.Count == 0) return -1;
        if (Repeat == RepeatMode.One && _currentIndex >= 0) return _currentIndex;

        if (_shuffle)
        {
            if (_shuffleOrder is null || _shufflePos <= 0) return -1;
            return _shuffleOrder[_shufflePos - 1];
        }

        var prev = _currentIndex - 1;
        if (prev < 0)
            return Repeat == RepeatMode.All ? _items.Count - 1 : -1;
        return prev;
    }

    internal void LoadPlaylist(Playlist playlist)
    {
        Clear();
        AddRange(playlist.Items.Select(i => i.Path));
        if (_items.Count > 0) SetCurrent(0);
    }

    internal Playlist SaveAsPlaylist(string name, MusicLibrary? library = null)
    {
        var pl = new Playlist { Name = name };
        foreach (var path in _items)
        {
            var track = library?.Find(path);
            pl.Items.Add(new PlaylistItem
            {
                Path            = path,
                Title           = track?.Title,
                Artist          = track?.Artist,
                DurationSeconds = track?.DurationSeconds ?? 0,
            });
        }
        return pl;
    }

    private void RebuildShuffle()
    {
        _shuffleOrder = Enumerable.Range(0, _items.Count).OrderBy(_ => _random.Next()).ToList();
        _shufflePos = _currentIndex >= 0 ? _shuffleOrder.IndexOf(_currentIndex) : -1;
        if (_shufflePos < 0 && _shuffleOrder.Count > 0) _shufflePos = 0;
    }
}
