using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Spectralis.Core.Integrations.Spotify;

namespace Spectralis.App.Services;

/// <summary>Shared by queue preloading and offline exports. Only complete audio is published; active readers hold leases.</summary>
public sealed class QueueAudioCache
{
    public static QueueAudioCache Shared { get; } = new();
    public const long LimitBytes = 512L * 1024 * 1024;
    private readonly string root;
    private readonly IDjSourceService sources;
    private readonly long capacity;
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 32).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    private readonly object files = new();
    private readonly Dictionary<string, int> readers = new(StringComparer.Ordinal);
    public QueueAudioCache(string? directory = null, IDjSourceService? sources = null, long capacity = LimitBytes)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        root = Path.GetFullPath(directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectralis", "queue-audio"));
        this.sources = sources ?? new DjSourceService();
    }
    private static string Key(string reference) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(reference))).ToLowerInvariant();

    public CachedQueueAudio? TryAcquire(string reference)
    {
        lock (files)
        {
            try
            {
                var key = Key(reference);
                var manifest = Path.Combine(root, key + ".json");
                if (!File.Exists(manifest)) return null;
                var result = JsonSerializer.Deserialize<RemoteAudioResolveResult>(File.ReadAllText(manifest));
                if (result?.CachedAudioPath is not { } path || !Path.GetFullPath(path).StartsWith(root + Path.DirectorySeparatorChar,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) || new FileInfo(path).Length <= 0) return null;
                File.SetLastWriteTimeUtc(manifest, DateTime.UtcNow);
                return Lease(result);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
        }
    }

    public async Task<CachedQueueAudio> AcquireAsync(DjSource source, IProgress<string>? progress, CancellationToken ct)
    {
        if (SpotifyPlaybackLink.TryParse(source.Reference, out _) && !BetaFeatures.SpotifyMatchup)
            throw new InvalidOperationException("Enable Spotify Matchup in Beta features to download matched audio.");
        var key = Key(source.Reference);
        var gate = gates[Convert.ToInt32(key[..2], 16) % gates.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (TryAcquire(source.Reference) is { } cached) return cached;
            Directory.CreateDirectory(root);
            using var prepared = await sources.PrepareAsync(source, progress, ct);
            var length = new FileInfo(prepared.Path).Length;
            if (length == 0) throw new IOException("The source returned an empty audio file.");
            if (length > capacity) throw new IOException("This track exceeds the audio cache's per-track size limit.");
            var extension = Path.GetExtension(prepared.Path);
            if (extension.Length is < 2 or > 12 || extension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))) extension = ".audio";
            var destination = Path.Combine(root, key + extension);
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".partial";
            try
            {
                await using (var input = new FileStream(prepared.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await input.CopyToAsync(output, ct);
                ct.ThrowIfCancellationRequested();
                var original = prepared.Resolved ?? new RemoteAudioResolveResult(RemoteAudioServiceKind.Generic,
                    prepared.Service, prepared.Identity, destination, extension, prepared.Service,
                    prepared.Title, prepared.Artist, null, null, TimeSpan.FromMilliseconds(source.DurationMs), null);
                var result = original with { AudioUrl = destination, CachedAudioPath = destination, MetadataOnly = false, WebViewEmbedHtml = null };
                lock (files)
                {
                    File.Move(temporary, destination, overwrite: true);
                    File.WriteAllText(Path.Combine(root, key + ".json"), JsonSerializer.Serialize(result));
                    var lease = Lease(result);
                    Trim();
                    return lease;
                }
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    private CachedQueueAudio Lease(RemoteAudioResolveResult result)
    {
        var path = result.CachedAudioPath!;
        var lease = new CachedQueueAudio(result, () =>
        {
            lock (files)
            {
                if (--readers[path] == 0) readers.Remove(path);
                try { Trim(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        });
        readers[path] = readers.GetValueOrDefault(path) + 1;
        return lease;
    }

    private void Trim()
    {
        var audio = Directory.EnumerateFiles(root).Where(p => !p.EndsWith(".json") && !p.EndsWith(".partial")).Select(p => new FileInfo(p)).ToList();
        var size = audio.Sum(f => f.Length);
        foreach (var file in audio.OrderBy(f => File.GetLastWriteTimeUtc(Path.ChangeExtension(f.FullName, ".json"))))
        {
            if (size <= capacity) break;
            if (readers.ContainsKey(file.FullName)) continue;
            try
            {
                // A current/next track or offline copy owns a read lease and cannot be evicted.
                using (new FileStream(file.FullName, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                File.Delete(file.FullName);
                File.Delete(Path.ChangeExtension(file.FullName, ".json"));
                size -= file.Length;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

public sealed class CachedQueueAudio : IDisposable
{
    private readonly FileStream lease;
    private Action? released;
    public RemoteAudioResolveResult Result { get; }
    public string Path => Result.CachedAudioPath!;
    public CachedQueueAudio(RemoteAudioResolveResult result, Action? released = null)
    {
        Result = result;
        this.released = released;
        lease = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }
    public void Dispose() { lease.Dispose(); Interlocked.Exchange(ref released, null)?.Invoke(); }
}
