using System.Security.Cryptography;
using System.Text;
using Spectralis.Core.Playlists;

namespace Spectralis.App.Services;

public sealed record PlaylistDownloadProgress(int Completed, int Total, string Track);
public sealed record PlaylistDownloadResult(int Downloaded, int Total, IReadOnlyList<string> Failures);

public sealed class PlaylistOfflineService(QueueAudioCache? cache = null)
{
    private readonly QueueAudioCache cache = cache ?? QueueAudioCache.Shared;

    public async Task<PlaylistDownloadResult> DownloadAsync(Playlist playlist, string parentDirectory,
        Action<Playlist> save, IProgress<PlaylistDownloadProgress>? progress, CancellationToken ct)
    {
        var folder = Path.Combine(Path.GetFullPath(parentDirectory), $"{SafeName(playlist.Name)}-{playlist.Id:N}");
        Directory.CreateDirectory(folder);
        playlist.OfflineDirectory = folder;
        var failures = new List<string>();
        var completed = 0;
        var items = playlist.Items.ToArray();
        try
        {
            for (var index = 0; index < items.Length; index++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[index];
                var title = item.Title ?? $"Track {index + 1}";
                progress?.Report(new(completed, items.Length, title));
                try
                {
                    if (item.OfflinePath is { } existing && File.Exists(existing) && new FileInfo(existing).Length > 0
                        && Path.GetDirectoryName(existing) == folder)
                    {
                        completed++;
                        continue;
                    }
                    using var audio = await cache.AcquireAsync(new DjSource(item.SourceReference,
                        Title: item.Title, Artist: item.Artist, DurationMs: (int)Math.Clamp(item.DurationSeconds * 1000, 0, int.MaxValue)), null, ct);
                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(item.SourceReference)))[..12];
                    var name = $"{index + 1:D4} - {SafeName(title)}-{hash}{Path.GetExtension(audio.Path)}";
                    var destination = Path.Combine(folder, name);
                    // Existing unrelated files are never overwritten. Checkpointed downloads above are reused.
                    if (File.Exists(destination)) destination = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(name)}-{Guid.NewGuid():N}{Path.GetExtension(name)}");
                    var temporary = destination + ".partial";
                    try
                    {
                        await using (var input = new FileStream(audio.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await input.CopyToAsync(output, ct);
                        ct.ThrowIfCancellationRequested();
                        File.Move(temporary, destination);
                        item.OfflinePath = destination;
                        save(playlist);
                        completed++;
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidOperationException or UnauthorizedAccessException)
                {
                    failures.Add($"{title}: {ex.Message}");
                }
            }
        }
        finally
        {
            // The portable M3U contains only completed local files, including after cancellation.
            var lines = new List<string> { "#EXTM3U" };
            foreach (var item in items.Where(i => i.OfflinePath is not null && File.Exists(i.OfflinePath)))
            {
                lines.Add($"#EXTINF:{(int)item.DurationSeconds},{(item.Title ?? "Track").Replace('\n', ' ').Replace('\r', ' ')}");
                lines.Add(Path.GetRelativePath(folder, item.OfflinePath!));
            }
            var manifest = Path.Combine(folder, "playlist.m3u8");
            File.WriteAllLines(manifest + ".partial", lines, new UTF8Encoding(false));
            File.Move(manifest + ".partial", manifest, overwrite: true);
            save(playlist);
        }
        progress?.Report(new(completed, items.Length, string.Empty));
        return new(completed, items.Length, failures);
    }

    private static string SafeName(string name)
    {
        var value = new string(name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).Take(70).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(value) ? "Playlist" : value;
    }
}
