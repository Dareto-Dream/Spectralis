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

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 16,
    };

    /// <summary>Pretty JSON for a document, always stamped with the current format name and version.</summary>
    public static string Serialize(ReactiveTimelineDocument document)
    {
        document.Format = ReactiveFormat.FormatName;
        document.FormatVersion = ReactiveFormat.FormatVersion;
        return JsonSerializer.Serialize(document, WriteOptions);
    }

    /// <summary>
    /// Writes the sidecar next to <paramref name="audioPath"/> (temp file then move, so a crash mid-write
    /// never leaves a half-written sidecar that fails to load). Refuses documents the loader would reject.
    /// </summary>
    public static string SaveSidecar(string audioPath, ReactiveTimelineDocument document)
    {
        var json = Serialize(document);
        if (Parse(json) is null)
        {
            throw new InvalidDataException("The timeline is not valid and was not saved.");
        }

        var path = GetSidecarPath(audioPath);
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
        return path;
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
