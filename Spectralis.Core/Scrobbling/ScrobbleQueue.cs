using System.Text.Json;

namespace Spectralis.Core.Scrobbling;

/// <summary>
/// Offline scrobble queue + capped listen history, persisted as JSON in the
/// same %LOCALAPPDATA%\Spectralis files the legacy app used.
/// </summary>
public sealed class ScrobbleQueue
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static string QueuePath { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "scrobble-queue.json");

    public static string HistoryPath { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "scrobble-history.json");

    private readonly List<ScrobbleRecord> _pending = [];

    public int Count => _pending.Count;

    public void Load()
    {
        try
        {
            if (!File.Exists(QueuePath))
            {
                return;
            }

            var loaded = JsonSerializer.Deserialize<List<ScrobbleRecord>>(File.ReadAllText(QueuePath), JsonOpts);
            if (loaded is not null)
            {
                _pending.AddRange(loaded);
            }
        }
        catch
        {
        }
    }

    public void Enqueue(ScrobbleRecord record)
    {
        _pending.Add(record);
        Save();
    }

    public List<ScrobbleRecord> Drain()
    {
        var batch = _pending.ToList();
        _pending.Clear();
        Save();
        return batch;
    }

    public void RestoreAll(IEnumerable<ScrobbleRecord> records)
    {
        _pending.InsertRange(0, records);
        Save();
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(QueuePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(QueuePath, JsonSerializer.Serialize(_pending, JsonOpts));
        }
        catch
        {
        }
    }

    public static void AppendHistory(ScrobbleRecord record)
    {
        try
        {
            var dir = Path.GetDirectoryName(HistoryPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<ScrobbleRecord> history = [];
            if (File.Exists(HistoryPath))
            {
                history = JsonSerializer.Deserialize<List<ScrobbleRecord>>(File.ReadAllText(HistoryPath), JsonOpts) ?? [];
            }

            history.Add(record);

            // Keep at most 10,000 entries (roughly 1-2 years of daily listening).
            if (history.Count > 10_000)
            {
                history.RemoveRange(0, history.Count - 10_000);
            }

            File.WriteAllText(HistoryPath, JsonSerializer.Serialize(history, JsonOpts));
        }
        catch
        {
        }
    }

    public static List<ScrobbleRecord> LoadHistory()
    {
        try
        {
            if (!File.Exists(HistoryPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<ScrobbleRecord>>(File.ReadAllText(HistoryPath), JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
