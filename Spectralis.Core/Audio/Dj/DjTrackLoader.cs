using System.Text;
using System.Text.RegularExpressions;
using Spectralis.Core.Integrations;

namespace Spectralis.Core.Audio.Dj;

/// <summary>One loading policy for the picker, dropped files and library tracks.</summary>
public static class DjTrackLoader
{
    public static async Task<IReadOnlyList<DjStemInput>> ResolveAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (paths.Count is < 1 or > 8) throw new ArgumentException("Choose between one and eight audio files or stems.");
        foreach (var path in paths)
            if (!File.Exists(path)) throw new FileNotFoundException("Audio file not found.", path);

        // Selecting several files explicitly declares a stem set. Preserve the
        // filenames (album/song title tags are often identical across all stems).
        if (paths.Count > 1)
            return paths.Select(path => new DjStemInput(path, Path.GetFileNameWithoutExtension(path))).ToArray();

        var source = paths[0];
        var streams = await ProbeAsync(source, ct).ConfigureAwait(false);
        var selected = streams.Count == 1 ? streams : streams.Where(s => !s.IsMaster).ToArray();
        if (selected.Count == 0) selected = [streams[0]];
        if (selected.Count > 8) throw new NotSupportedException("This file contains more than eight stems. A deck supports up to eight.");
        return selected.Select(s => new DjStemInput(source, s.Name, s.AudioIndex)).ToArray();
    }

    public static async Task<IReadOnlyList<DjContainerStream>> ProbeAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (!File.Exists(path)) throw new FileNotFoundException("Audio file not found.", path);
        var ffmpeg = FfmpegLocator.FindExecutable() ?? throw new FileNotFoundException("Bundled FFmpeg is missing.");
        // Input-only inspection exits nonzero because no output was requested.
        // Keep all stream boundaries so video/data metadata cannot name an audio stem.
        var result = await SafeProcessRunner.RunAsync(ffmpeg, ["-hide_banner", "-nostdin", "-i", path],
            TimeSpan.FromSeconds(30), cancellationToken: ct, outputEncoding: Encoding.UTF8).ConfigureAwait(false);
        if (result.TimedOut) throw new TimeoutException("Audio track inspection timed out.");
        ct.ThrowIfCancellationRequested();
        var streams = ParseStreams(result.Stderr, path).ToArray();
        if (streams.Length == 0) throw new InvalidOperationException("No audio tracks were found in this file.");

        var names = await Task.Run(() => Mp4StemMetadata.ReadNames(path, ct), ct).ConfigureAwait(false);
        var nativeStems = names.Count > 0 && streams.Length == names.Count + 1;
        var nativeExtension = path.EndsWith(".stem.mp4", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".stem.m4a", StringComparison.OrdinalIgnoreCase);
        if (nativeStems || nativeExtension && streams.Length == 5)
        {
            // NI Stems stores the full mix first; its JSON names describe only
            // the following stems. Never sum the master back into those stems.
            streams[0] = streams[0] with { IsMaster = true };
            for (var i = 1; i < streams.Length; i++)
                streams[i] = streams[i] with { Name = nativeStems ? names[i - 1] ?? streams[i].Name : streams[i].Name, IsMaster = false };
        }
        return streams;
    }

    internal static IReadOnlyList<DjContainerStream> ParseStreams(string inspection, string path)
    {
        var boundaries = Regex.Matches(inspection, @"(?m)^\s*Stream #0:\d+[^\r\n]*");
        var streams = new List<DjContainerStream>();
        for (var i = 0; i < boundaries.Count; i++)
        {
            var header = boundaries[i];
            var audio = Regex.Match(header.Value, @": Audio: (.+)");
            if (!audio.Success) continue;
            var end = i + 1 < boundaries.Count ? boundaries[i + 1].Index : inspection.Length;
            var metadata = inspection[(header.Index + header.Length)..end];
            string? Tag(string key) => Regex.Match(metadata, @"(?im)^\s*" + key + @"\s*:\s*([^\r\n]+)") is { Success: true } match
                ? match.Groups[1].Value.Trim() : null;
            var name = Tag("title") ?? Tag("name");
            if (string.IsNullOrWhiteSpace(name))
            {
                var handler = Tag("handler_name");
                if (!string.IsNullOrWhiteSpace(handler) && !IsGenericHandler(handler)) name = handler;
            }
            var isMaster = name is not null && Regex.IsMatch(name, @"^(master(?: mix)?|full mix|mix(?:ture)?|original(?: mix)?|stereo mix)$", RegexOptions.IgnoreCase);
            streams.Add(new(streams.Count, name ?? "", audio.Groups[1].Value.Trim(), isMaster));
        }
        // Unnamed tracks cannot supply an instrument name. Retain the source
        // filename and use a suffix only when needed to distinguish streams.
        return streams.Select(s => string.IsNullOrWhiteSpace(s.Name)
            ? s with { Name = Path.GetFileNameWithoutExtension(path) + (streams.Count > 1 ? $" · stem {s.AudioIndex + 1}" : "") }
            : s).ToArray();
    }

    private static bool IsGenericHandler(string name) =>
        new[] { "SoundHandler", "AudioHandler", "GPAC ISO Audio Handler", "Core Media Audio" }.Contains(name, StringComparer.OrdinalIgnoreCase) ||
        name.StartsWith("ISO Media file", StringComparison.OrdinalIgnoreCase);
}
