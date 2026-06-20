namespace Spectralis.Core.Audio;

public sealed class PlaybackStateChangedEventArgs : EventArgs
{
    public PlaybackStateChangedEventArgs(PlaybackState from, PlaybackState to, string? errorMessage)
    {
        From = from;
        To = to;
        ErrorMessage = errorMessage;
    }

    public PlaybackState From { get; }
    public PlaybackState To { get; }
    public string? ErrorMessage { get; }
}

/// <summary>
/// Validates and tracks playback state transitions. The audio engine drives this;
/// ViewModels observe it. Invalid transitions throw so engine bugs surface in tests
/// instead of leaving the UI in an impossible state.
/// </summary>
public sealed class PlaybackStateMachine
{
    private static readonly Dictionary<PlaybackState, PlaybackState[]> Allowed = new()
    {
        [PlaybackState.Idle] = new[] { PlaybackState.Loading },
        [PlaybackState.Loading] = new[] { PlaybackState.Playing, PlaybackState.Error, PlaybackState.Stopped },
        [PlaybackState.Playing] = new[] { PlaybackState.Paused, PlaybackState.Stopped, PlaybackState.Error, PlaybackState.Loading },
        [PlaybackState.Paused] = new[] { PlaybackState.Playing, PlaybackState.Stopped, PlaybackState.Error, PlaybackState.Loading },
        [PlaybackState.Stopped] = new[] { PlaybackState.Loading, PlaybackState.Idle, PlaybackState.Playing },
        [PlaybackState.Error] = new[] { PlaybackState.Loading, PlaybackState.Idle, PlaybackState.Stopped },
    };

    private readonly object _gate = new();

    public PlaybackState State { get; private set; } = PlaybackState.Idle;
    public string? LastError { get; private set; }

    public event EventHandler<PlaybackStateChangedEventArgs>? StateChanged;

    public bool CanTransitionTo(PlaybackState next)
    {
        lock (_gate)
        {
            return State == next || Allowed[State].Contains(next);
        }
    }

    /// <summary>Moves to <paramref name="next"/>. Throws on an illegal transition.</summary>
    public void TransitionTo(PlaybackState next, string? errorMessage = null)
    {
        PlaybackStateChangedEventArgs? args = null;
        lock (_gate)
        {
            if (State == next)
            {
                return;
            }

            if (!Allowed[State].Contains(next))
            {
                throw new InvalidOperationException($"Illegal playback transition {State} -> {next}.");
            }

            args = new PlaybackStateChangedEventArgs(State, next, errorMessage);
            State = next;
            LastError = next == PlaybackState.Error ? errorMessage : null;
        }

        StateChanged?.Invoke(this, args);
    }

    /// <summary>Moves to <paramref name="next"/> if legal; returns false instead of throwing.</summary>
    public bool TryTransitionTo(PlaybackState next, string? errorMessage = null)
    {
        if (!CanTransitionTo(next))
        {
            return false;
        }

        TransitionTo(next, errorMessage);
        return true;
    }
}
