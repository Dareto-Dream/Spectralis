namespace Spectralis.Core.SharedPlay;

/// <summary>Room uploads become local playback files before their temporary server copy is removed.</summary>
public sealed class SharedPlayListenerUploads : IDisposable
{
    private readonly SharedPlayCdnClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly HashSet<string> queued = [];
    private readonly Dictionary<string, FileStream> leases = [];
    public SharedPlayListenerUploads(SharedPlayCdnClient client) => this.client = client;

    public async Task ReceiveAsync(SharedPlayRoomSession session, SharedPlayQueueItem item,
        Func<string, Task> accept, bool playNow, CancellationToken ct)
    {
        var url = ValidateUrl(session, item);
        var filename = Uri.UnescapeDataString(url.Segments[^1]);
        if (string.IsNullOrWhiteSpace(filename) || filename != Path.GetFileName(filename) || filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("The listener file has an invalid filename.");
        var folder = Path.Combine(Path.GetTempPath(), "Spectralis", "shared-play-uploads", session.RoomCode + "-" + item.SourceId);
        var path = Path.Combine(folder, filename);
        await gate.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(folder);
            if (!leases.ContainsKey(folder)) leases[folder] = new FileStream(Path.Combine(folder, ".active"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read);
            if (!File.Exists(path)) await client.DownloadListenerUploadAsync(session, url, path, ct);
            if (playNow || !queued.Contains(path))
            {
                await accept(path);
                queued.Add(path);
            }
            // A retry after an acknowledgment failure must never enqueue the same file twice.
            await client.AcknowledgeListenerUploadAsync(session, item.SourceId!, ct);
        }
        finally { gate.Release(); }
    }

    internal static Uri ValidateUrl(SharedPlayRoomSession session, SharedPlayQueueItem item)
    {
        if (SharedPlayDefaults.NormalizeRoomCode(session.RoomCode) != session.RoomCode ||
            item.SourceKind != "upload" || !Guid.TryParseExact(item.SourceId, "D", out _) ||
            !Uri.TryCreate(item.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps ||
            url.GetLeftPart(UriPartial.Authority) != session.StateUrl.GetLeftPart(UriPartial.Authority) ||
            !string.IsNullOrEmpty(url.UserInfo) || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.Fragment) ||
            !url.AbsolutePath.StartsWith($"/shared-play/v2/sessions/{session.RoomCode}/uploads/{item.SourceId}/audio/", StringComparison.Ordinal) ||
            url.Segments.Length != 9)
            throw new InvalidOperationException("The listener upload does not belong to this room.");
        return url;
    }

    public void Dispose()
    {
        foreach (var lease in leases.Values) lease.Dispose();
        leases.Clear();
    }
}
