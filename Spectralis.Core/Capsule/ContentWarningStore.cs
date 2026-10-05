using System.Text.Json;

namespace Spectralis.Core.Capsule;

/// <summary>
/// Remembers which world content warnings the listener has already accepted, keyed by the
/// warning's wording hash (<see cref="Spectralis.Core.Embedded.EmbeddedContentWarning.Key"/>) so a
/// rewritten warning asks again. Lives next to <c>trusted-creators.json</c>. A broken or missing
/// file just means "ask again" — never an error, and never a reason to skip the warning.
/// </summary>
public sealed class ContentWarningStore
{
    private readonly string _path;
    private readonly object _gate = new();

    public ContentWarningStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis",
            "content-warnings.json");
    }

    public bool IsAccepted(string key)
    {
        lock (_gate)
        {
            return Read().Contains(key, StringComparer.Ordinal);
        }
    }

    public void Accept(string key)
    {
        lock (_gate)
        {
            var keys = Read();
            if (keys.Contains(key, StringComparer.Ordinal))
            {
                return;
            }

            keys.Add(key);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(keys));
                File.Move(tmp, _path, overwrite: true);
            }
            catch
            {
                // can't persist: the warning simply shows again next time
            }
        }
    }

    private List<string> Read()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch
        {
            return [];
        }
    }
}
