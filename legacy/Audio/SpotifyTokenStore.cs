using System.IO;
using System.Text.Json;

namespace Spectralis;

internal sealed class SpotifyTokenStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StorePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Spectralis", "spotify.json");

    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? AccountDisplayName { get; set; }
    public string? AccountEmail { get; set; }

    public bool HasValidToken =>
        !string.IsNullOrEmpty(AccessToken) && DateTime.UtcNow < ExpiresAt.AddSeconds(-30);

    public static SpotifyTokenStore Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return new SpotifyTokenStore();
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<SpotifyTokenStore>(json) ?? new SpotifyTokenStore();
        }
        catch { return new SpotifyTokenStore(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            File.WriteAllText(StorePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch { }
    }

    public void Clear()
    {
        AccessToken = null;
        RefreshToken = null;
        ExpiresAt = default;
        AccountDisplayName = null;
        AccountEmail = null;
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
    }
}
