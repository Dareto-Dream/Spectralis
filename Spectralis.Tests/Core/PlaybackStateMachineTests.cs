using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public class PlaybackStateMachineTests
{
    [Fact]
    public void StartsIdle()
    {
        var sm = new PlaybackStateMachine();
        Assert.Equal(PlaybackState.Idle, sm.State);
        Assert.Null(sm.LastError);
    }

    [Theory]
    [InlineData(PlaybackState.Idle, PlaybackState.Loading, true)]
    [InlineData(PlaybackState.Idle, PlaybackState.Playing, false)]
    [InlineData(PlaybackState.Idle, PlaybackState.Paused, false)]
    [InlineData(PlaybackState.Loading, PlaybackState.Playing, true)]
    [InlineData(PlaybackState.Loading, PlaybackState.Error, true)]
    [InlineData(PlaybackState.Loading, PlaybackState.Stopped, true)]
    [InlineData(PlaybackState.Loading, PlaybackState.Paused, false)]
    [InlineData(PlaybackState.Playing, PlaybackState.Paused, true)]
    [InlineData(PlaybackState.Playing, PlaybackState.Stopped, true)]
    [InlineData(PlaybackState.Playing, PlaybackState.Loading, true)]
    [InlineData(PlaybackState.Paused, PlaybackState.Playing, true)]
    [InlineData(PlaybackState.Paused, PlaybackState.Idle, false)]
    [InlineData(PlaybackState.Stopped, PlaybackState.Loading, true)]
    [InlineData(PlaybackState.Stopped, PlaybackState.Playing, true)]
    [InlineData(PlaybackState.Stopped, PlaybackState.Paused, false)]
    [InlineData(PlaybackState.Error, PlaybackState.Loading, true)]
    [InlineData(PlaybackState.Error, PlaybackState.Playing, false)]
    public void ValidatesTransitions(PlaybackState from, PlaybackState to, bool allowed)
    {
        var sm = DriveTo(from);
        Assert.Equal(allowed, sm.CanTransitionTo(to));
        Assert.Equal(allowed, sm.TryTransitionTo(to));
        Assert.Equal(allowed ? to : from, sm.State);
    }

    [Fact]
    public void IllegalTransitionThrows()
    {
        var sm = new PlaybackStateMachine();
        Assert.Throws<InvalidOperationException>(() => sm.TransitionTo(PlaybackState.Playing));
    }

    [Fact]
    public void SameStateTransitionIsNoOpAndRaisesNoEvent()
    {
        var sm = DriveTo(PlaybackState.Playing);
        var raised = 0;
        sm.StateChanged += (_, _) => raised++;
        sm.TransitionTo(PlaybackState.Playing);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void StateChangedReportsFromToAndError()
    {
        var sm = DriveTo(PlaybackState.Loading);
        PlaybackStateChangedEventArgs? seen = null;
        sm.StateChanged += (_, e) => seen = e;

        sm.TransitionTo(PlaybackState.Error, "decoder failed");

        Assert.NotNull(seen);
        Assert.Equal(PlaybackState.Loading, seen!.From);
        Assert.Equal(PlaybackState.Error, seen.To);
        Assert.Equal("decoder failed", seen.ErrorMessage);
        Assert.Equal("decoder failed", sm.LastError);
    }

    [Fact]
    public void LastErrorClearsWhenLeavingErrorState()
    {
        var sm = DriveTo(PlaybackState.Loading);
        sm.TransitionTo(PlaybackState.Error, "boom");
        sm.TransitionTo(PlaybackState.Loading);
        Assert.Null(sm.LastError);
    }

    private static PlaybackStateMachine DriveTo(PlaybackState target)
    {
        var sm = new PlaybackStateMachine();
        switch (target)
        {
            case PlaybackState.Idle:
                break;
            case PlaybackState.Loading:
                sm.TransitionTo(PlaybackState.Loading);
                break;
            case PlaybackState.Playing:
                sm.TransitionTo(PlaybackState.Loading);
                sm.TransitionTo(PlaybackState.Playing);
                break;
            case PlaybackState.Paused:
                sm.TransitionTo(PlaybackState.Loading);
                sm.TransitionTo(PlaybackState.Playing);
                sm.TransitionTo(PlaybackState.Paused);
                break;
            case PlaybackState.Stopped:
                sm.TransitionTo(PlaybackState.Loading);
                sm.TransitionTo(PlaybackState.Stopped);
                break;
            case PlaybackState.Error:
                sm.TransitionTo(PlaybackState.Loading);
                sm.TransitionTo(PlaybackState.Error, "test");
                break;
        }

        return sm;
    }
}
