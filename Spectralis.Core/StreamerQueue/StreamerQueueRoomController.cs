namespace Spectralis.Core.StreamerQueue;

public sealed class StreamerQueueRoomController : IDisposable
{
    private readonly StreamerQueueClient client;
    private readonly SemaphoreSlim gate = new(1, 1);

    private Uri cdnBaseUri = new("https://cdn.spectralis.app");
    private string? roomId;
    private string? ownerToken;

    public SqRoom? LastSnapshot { get; private set; }
    public string? LastError { get; private set; }

    public StreamerQueueRoomController() : this(new StreamerQueueClient()) { }

    internal StreamerQueueRoomController(StreamerQueueClient client) => this.client = client;

    public void Dispose() => client.Dispose();

    public void Configure(Uri baseUri, string? roomId, string? ownerToken)
    {
        cdnBaseUri = baseUri;
        this.roomId = roomId;
        this.ownerToken = ownerToken;
    }

    public async Task<SqCreateRoomResponse> CreateRoomAsync(Uri baseUri, CancellationToken ct)
    {
        var result = await client.CreateRoomAsync(baseUri, ct);
        cdnBaseUri = baseUri;
        roomId = result.RoomId;
        ownerToken = result.OwnerToken;
        return result;
    }

    public async Task<SqRoom?> PollAsync(CancellationToken ct)
    {
        if (roomId is null) return null;
        await gate.WaitAsync(ct);
        try
        {
            var snapshot = await client.GetRoomAsync(cdnBaseUri, roomId, ownerToken, ct);
            LastSnapshot = snapshot;
            LastError = null;
            return snapshot;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return LastSnapshot;
        }
        finally { gate.Release(); }
    }

    public async Task<SqRoom> SaveSettingsAsync(bool enabled, SqSettings settings, string? channelId, CancellationToken ct)
    {
        EnsureOwner();
        var result = await client.PutSettingsAsync(cdnBaseUri, roomId!, ownerToken!, enabled, settings, channelId, ct);
        LastSnapshot = result;
        return result;
    }

    public async Task ApproveAsync(string submissionId, CancellationToken ct)
    {
        EnsureOwner();
        await client.ApproveAsync(cdnBaseUri, roomId!, submissionId, ownerToken!, ct);
    }

    public async Task RejectAsync(string submissionId, CancellationToken ct)
    {
        EnsureOwner();
        await client.RejectAsync(cdnBaseUri, roomId!, submissionId, ownerToken!, ct);
    }

    public async Task DeleteAsync(string submissionId, CancellationToken ct)
    {
        EnsureOwner();
        await client.DeleteSubmissionAsync(cdnBaseUri, roomId!, submissionId, ownerToken!, ct);
    }

    public async Task ReorderAsync(IEnumerable<string> orderedIds, CancellationToken ct)
    {
        EnsureOwner();
        await client.SetOrderAsync(cdnBaseUri, roomId!, ownerToken!, orderedIds, ct);
    }

    public async Task SetNowPlayingAsync(string? submissionId, CancellationToken ct)
    {
        EnsureOwner();
        await client.SetNowPlayingAsync(cdnBaseUri, roomId!, ownerToken!, submissionId, ct);
    }

    public async Task<SqStripeConnectResponse> GetStripeConnectUrlAsync(CancellationToken ct)
    {
        EnsureOwner();
        return await client.GetStripeConnectUrlAsync(cdnBaseUri, roomId!, ownerToken!, ct);
    }

    public async Task StripeDisconnectAsync(CancellationToken ct)
    {
        EnsureOwner();
        await client.StripeDisconnectAsync(cdnBaseUri, roomId!, ownerToken!, ct);
    }

    private void EnsureOwner()
    {
        if (roomId is null || ownerToken is null)
            throw new InvalidOperationException("No SQ room configured. Create or load a room first.");
    }
}
