namespace Spectralis.Core.Satellite;

/// <summary>A stable per-install identifier for a Satellite receiver client, sent in its
/// <see cref="SatelliteHelloMessage"/> — mirrors <c>SharedPlayClientIdentity</c>'s pattern
/// exactly (same minting/caching shape, different file).</summary>
public static class SatelliteDeviceIdentity
{
    /// <summary>Test seam — set to redirect the id file.</summary>
    public static string? PathOverride { get; set; }

    private static readonly object Gate = new();
    private static string? _cached;

    private static string FilePath =>
        PathOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "Satellite", "device-id.txt");

    public static string Get()
    {
        if (_cached is { Length: > 0 })
        {
            return _cached;
        }

        lock (Gate)
        {
            if (_cached is { Length: > 0 })
            {
                return _cached;
            }

            var path = FilePath;
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path).Trim();
                    if (existing.Length is >= 8 and <= 64)
                    {
                        return _cached = existing;
                    }
                }
            }
            catch
            {
                // fall through and mint a new one
            }

            var minted = "sat-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, minted);
            }
            catch
            {
                // non-fatal: a volatile id still works for this session
            }

            return _cached = minted;
        }
    }
}
