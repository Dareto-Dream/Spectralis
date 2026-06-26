namespace Spectralis.Core.Common;

public static class FormatLabel
{
    /// <summary>Human-readable container label from a file extension.</summary>
    public static string FromExtension(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp3" => "MP3",
            ".wav" => "WAV",
            ".flac" => "FLAC",
            ".ogg" or ".oga" => "Ogg Vorbis",
            ".opus" => "Opus",
            ".m4a" => "M4A",
            ".m4b" => "M4B",
            ".m4p" => "M4P",
            ".aac" => "AAC",
            ".adts" => "AAC / ADTS",
            ".mid" or ".midi" => "MIDI",
            ".kar" => "MIDI Karaoke",
            ".wma" => "WMA",
            ".asf" => "ASF",
            ".webm" => "WebM audio",
            ".mp4" => "MP4 audio",
            ".aif" or ".aifc" or ".aiff" => "AIFF",
            ".3gp" => "3GP audio",
            var ext when ext.Length > 1 => ext[1..].ToUpperInvariant(),
            _ => "Unknown",
        };

    /// <summary>Formats a byte count as B/KB/MB/GB with one decimal, data-face friendly.</summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.#} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):0.#} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.##} GB";
    }
}
