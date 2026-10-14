using System.Text.Json;
using Spectralis.Core.Audio.Dj;

namespace Spectralis.App.Services;

public sealed record DjCueData(long Modified, long Length, double Cue, double?[] HotCues, double Bpm, double BeatOffset);

public sealed class DjCueStore
{
    private readonly string _path;
    private readonly Dictionary<string, DjCueData> _tracks;
    public DjCueStore(string? path = null)
    {
        _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spectralis", "dj-cues.json");
        try { _tracks = JsonSerializer.Deserialize<Dictionary<string, DjCueData>>(File.ReadAllText(_path)) ?? new(); }
        catch { _tracks = new(); }
    }
    public DjCueData? Get(string path)
    {
        if (RemoteKey(path) is { } remote) return _tracks.GetValueOrDefault(remote);
        var file = new FileInfo(path);
        return _tracks.TryGetValue(Path.GetFullPath(path), out var data) && file.Exists &&
            data.Modified == file.LastWriteTimeUtc.Ticks && data.Length == file.Length ? data : null;
    }
    public void Save(DjDeckState state)
    {
        if (state.Path is not { } path) return;
        var remote = RemoteKey(path);
        if (remote is null && !File.Exists(path)) return;
        var file = remote is null ? new FileInfo(path) : null;
        _tracks[remote ?? Path.GetFullPath(path)] = new(file?.LastWriteTimeUtc.Ticks ?? 0, file?.Length ?? 0, state.Cue, state.HotCues,
            state.Settings.Bpm, state.Settings.BeatOffset);
        if (_tracks.Count > 2048) _tracks.Remove(_tracks.Keys.First());
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(_tracks));
        File.Move(temporary, _path, overwrite: true);
    }
    private static string? RemoteKey(string path) => Uri.TryCreate(path, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
}
