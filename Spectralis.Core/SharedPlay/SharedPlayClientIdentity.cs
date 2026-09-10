namespace Spectralis.Core.SharedPlay;

/// <summary>A stable per-install identifier for this Spectralis client, used to
/// name the participant in a collaborative room (and to keep a co-DJ grant
/// across reconnects). Mirrors the web player's localStorage client id.</summary>
public static class SharedPlayClientIdentity
{
    /// <summary>Test seam — set to redirect the id file.</summary>
    public static string? PathOverride { get; set; }

    private static readonly object Gate = new();
    private static string? _cached;

    private static string FilePath =>
        PathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "SharedPlay", "client-id.txt");

    public static string Get()
    {
        if (_cached is { Length: > 0 }) return _cached;
        lock (Gate)
        {
            if (_cached is { Length: > 0 }) return _cached;

            var path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length is >= 8 and <= 64)
                        return _cached = existing;
                }
            }
            catch { /* fall through and mint a new one */ }

            var minted = "sp-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, minted);
            }
            catch { /* non-fatal: a volatile id still works for this session */ }
            return _cached = minted;
        }
    }
}
