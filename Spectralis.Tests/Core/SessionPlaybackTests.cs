using Spectralis.Core.Sessions;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SessionPlaybackTests
{
    [Fact]
    public async Task RequestsWaitThroughBothTracksAndResumeAfterTheRound()
    {
        var calls = new List<string>();
        var playback = new SessionPlaybackCoordinator(
            () => Record("suspend"), () => Record("restore"), source => Record("play:" + source), source => Record("queue:" + source));
        Task Record(string value) { calls.Add(value); return Task.CompletedTask; }
        await playback.PlayRoundTrackAsync("A");
        Assert.False(await playback.RequestAsync("request", true));
        Assert.False(await playback.RequestAsync("request", true));
        await playback.PlayRoundTrackAsync("B");
        Assert.Equal(new[] { "suspend", "play:A", "play:B" }, calls);
        Assert.Equal(1, playback.WaitingCount);
        await playback.EndRoundAsync();
        Assert.Equal(new[] { "suspend", "play:A", "play:B", "restore", "queue:request" }, calls);
        Assert.False(playback.RoundOwnsPlayback);
        Assert.True(await playback.RequestAsync("next", true));
        Assert.Equal("play:next", calls[^1]);
    }

    [Fact]
    public async Task FailedResolveStillReservesRoundUntilHostReturnsToQueue()
    {
        var restored = false;
        var playback = new SessionPlaybackCoordinator(() => Task.CompletedTask,
            () => { restored = true; return Task.CompletedTask; }, _ => throw new IOException("Resolve failed"), _ => Task.CompletedTask);
        await Assert.ThrowsAsync<IOException>(() => playback.PlayRoundTrackAsync("A"));
        Assert.True(playback.RoundOwnsPlayback);
        Assert.False(await playback.RequestAsync("request", true));
        await playback.EndRoundAsync();
        Assert.True(restored);
        Assert.False(playback.RoundOwnsPlayback);
    }

    [Fact]
    public async Task RetryDoesNotRestoreQueueTwiceOrLosePendingRequests()
    {
        var restores = 0;
        var attempts = 0;
        var playback = new SessionPlaybackCoordinator(() => Task.CompletedTask,
            () => { restores++; return Task.CompletedTask; }, _ => Task.CompletedTask,
            _ => ++attempts == 1 ? Task.FromException(new IOException("Offline")) : Task.CompletedTask);
        await playback.PlayRoundTrackAsync("A");
        await playback.RequestAsync("request", false);
        await Assert.ThrowsAsync<IOException>(playback.EndRoundAsync);
        Assert.Equal(1, playback.WaitingCount);
        await playback.EndRoundAsync();
        Assert.Equal(1, restores);
        Assert.Equal(0, playback.WaitingCount);
    }

    [Fact]
    public async Task RequestArrivingDuringResolveCannotStealPlayback()
    {
        var resolving = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var played = new List<string>();
        var playback = new SessionPlaybackCoordinator(() => Task.CompletedTask, () => Task.CompletedTask,
            async source => { played.Add(source); resolving.SetResult(); await finish.Task; }, _ => Task.CompletedTask);
        var round = playback.PlayRoundTrackAsync("A");
        await resolving.Task;
        var request = playback.RequestAsync("interrupt", true);
        Assert.False(request.IsCompleted);
        finish.SetResult();
        await round;
        Assert.False(await request);
        Assert.Equal(new[] { "A" }, played);
    }
}
