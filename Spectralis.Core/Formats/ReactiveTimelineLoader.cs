using System.Text.Json;

namespace Spectralis.Core.Formats;

/// <summary>
/// Loads .spectralis-reactive.json sidecars. Untrusted input: size capped before
/// reading, structure validated before the document is accepted.
/// </summary>
public static class ReactiveTimelineLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 16,
    };

    public static string GetSidecarPath(string audioPath) =>
        Path.ChangeExtension(audioPath, ".spectralis-reactive.json");

    public static ReactiveTimelineDocument? LoadSidecar(string audioPath)
    {
        try
        {
            var path = GetSidecarPath(audioPath);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > ReactiveFormat.MaxSidecarBytes)
            {
                return null;
            }

            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public static ReactiveTimelineDocument? Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > ReactiveFormat.MaxSidecarBytes)
        {
            return null;
        }

        try
        {
            var document = JsonSerializer.Deserialize<ReactiveTimelineDocument>(json, Options);
            return document is { } parsed && parsed.IsValid() ? parsed : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
