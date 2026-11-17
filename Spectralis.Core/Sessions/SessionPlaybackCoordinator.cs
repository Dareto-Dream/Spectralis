namespace Spectralis.Core.Sessions;

/// <summary>One playback owner per session. Requests remain available while a round is active.</summary>
public sealed class SessionPlaybackCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Func<Task> suspendQueue;
    private readonly Func<Task> restoreQueue;
    private readonly Func<string, Task> play;
    private readonly Func<string, Task> enqueue;
    private readonly Queue<string> waiting = new();
    private bool queueRestored;

    public SessionPlaybackCoordinator(Func<Task> suspendQueue, Func<Task> restoreQueue,
        Func<string, Task> play, Func<string, Task> enqueue)
    {
        this.suspendQueue = suspendQueue;
        this.restoreQueue = restoreQueue;
        this.play = play;
        this.enqueue = enqueue;
    }

    public bool RoundOwnsPlayback { get; private set; }
    public int WaitingCount => waiting.Count;
    public event Action? Changed;

    public async Task PlayRoundTrackAsync(string source)
    {
        await gate.WaitAsync();
        try
        {
            if (!RoundOwnsPlayback)
            {
                await suspendQueue();
                RoundOwnsPlayback = true;
                Changed?.Invoke();
            }
            // A failed resolve keeps the round reserved. The host can retry or end the round.
            await play(source);
        }
        finally { gate.Release(); }
    }

    /// <returns>False when the request is waiting until the current round ends.</returns>
    public async Task<bool> RequestAsync(string source, bool playNow)
    {
        await gate.WaitAsync();
        try
        {
            if (RoundOwnsPlayback)
            {
                if (!waiting.Contains(source, StringComparer.Ordinal)) waiting.Enqueue(source);
                Changed?.Invoke();
                return false;
            }
            if (playNow) await play(source);
            else await enqueue(source);
            return true;
        }
        finally { gate.Release(); }
    }

    public async Task EndRoundAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (!RoundOwnsPlayback) return;
            // Keep ownership and the pending requests on failure so restoring can be retried.
            if (!queueRestored) { await restoreQueue(); queueRestored = true; }
            while (waiting.TryPeek(out var source))
            {
                await enqueue(source);
                waiting.Dequeue();
            }
            RoundOwnsPlayback = false;
            queueRestored = false;
            Changed?.Invoke();
        }
        finally { gate.Release(); }
    }
}
