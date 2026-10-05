namespace Spectralis.Core.Diagnostics;

/// <summary>
/// A bounded, thread-safe record of the calls the app has made. The newest <see cref="Capacity"/> entries are kept;
/// older ones fall off the end. Entries are immutable, so a snapshot never changes under whoever is reading it.
/// </summary>
public sealed class NetworkLogStore
{
    private readonly object _gate = new();
    private readonly List<NetworkEntry> _entries = new();
    private long _lastId;

    public NetworkLogStore(int capacity = 2000)
    {
        Capacity = Math.Max(1, capacity);
    }

    public int Capacity { get; }

    /// <summary>When false nothing is recorded and calls pass straight through.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Raised after any change, from whichever thread made it. Listeners should coalesce.</summary>
    public event Action? Changed;

    public IReadOnlyList<NetworkEntry> Snapshot()
    {
        lock (_gate) return _entries.ToArray();
    }

    /// <summary>Adds an entry, giving it the next id and (if it has none) the current time, and returns the id.</summary>
    public long Begin(NetworkEntry entry)
    {
        long id;
        lock (_gate)
        {
            id = ++_lastId;
            _entries.Add(entry with
            {
                Id = id,
                StartedAt = entry.StartedAt == default ? DateTimeOffset.Now : entry.StartedAt,
            });
            if (_entries.Count > Capacity) _entries.RemoveRange(0, _entries.Count - Capacity);
        }
        Changed?.Invoke();
        return id;
    }

    /// <summary>Replaces an entry with a changed copy. Does nothing if it has already fallen off the end.</summary>
    public void Update(long id, Func<NetworkEntry, NetworkEntry> change)
    {
        var found = false;
        lock (_gate)
        {
            for (var i = _entries.Count - 1; i >= 0; i--)
            {
                if (_entries[i].Id != id) continue;
                _entries[i] = change(_entries[i]) with { Id = id };
                found = true;
                break;
            }
        }
        if (found) Changed?.Invoke();
    }

    public void Clear()
    {
        lock (_gate) _entries.Clear();
        Changed?.Invoke();
    }
}
