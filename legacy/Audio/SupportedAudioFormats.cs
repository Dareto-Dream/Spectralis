using System.IO;

namespace Spectralis;

public static class SupportedAudioFormats
{
    public static readonly string[] Extensions =
    {
        ".aac",
        ".adts",
        ".aif",
        ".aifc",
        ".aiff",
        ".asf",
        ".flac",
        ".kar",
        ".m4a",
        ".m4b",
        ".m4p",
        ".mid",
        ".midi",
        ".mp3",
        ".mp4",
        ".oga",
        ".ogg",
        ".opus",
        ".wav",
        ".webm",
        ".wma",
        ".3gp"
    };

    public static string OpenFileDialogFilter { get; } =
        $"Audio Files|{string.Join(";", Extensions.Select(extension => $"*{extension}"))}|All Files|*.*";

    public static bool IsSupportedExtension(string path) =>
        Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
}
