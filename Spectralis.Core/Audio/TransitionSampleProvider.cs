using NAudio.Wave;

namespace Spectralis.Core.Audio;

/// <summary>
/// Head of the playback chain. Plays the current source and, once a next source is queued, hands
/// over to it without the output device ever stopping: either a sample-exact splice (gapless) or
/// an equal-power crossfade over <c>overlapSeconds</c>. Both sources must share this provider's
/// <see cref="WaveFormat"/> — the engine resamples/up-mixes the next track before queueing it.
/// </summary>
public sealed class TransitionSampleProvider : ISampleProvider
{
    private readonly object _gate = new();
    private readonly int _channels;

    private ISampleProvider _current;
    private ISampleProvider? _next;
    private Func<double>? _currentRemainingSeconds;
    private double _overlapSeconds;
    private int _overlapFrames;
    private int _fadeFrame;
    private bool _crossfading;
    private float[] _scratch = [];

    public TransitionSampleProvider(ISampleProvider current)
    {
        _current = current;
        _channels = current.WaveFormat.Channels;
        WaveFormat = current.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// The next source became audible (crossfade began, or the gapless splice happened).
    /// Raised on the audio thread — keep handlers short.
    /// </summary>
    public event Action? Transitioning;

    /// <summary>The outgoing source is no longer read from and can be disposed. Audio thread.</summary>
    public event Action? OutgoingFinished;

    public bool IsArmed
    {
        get { lock (_gate) return _next is not null; }
    }

    /// <summary>
    /// Queues <paramref name="next"/>. <paramref name="overlapSeconds"/> of 0 means gapless;
    /// otherwise crossfade starts when <paramref name="currentRemainingSeconds"/> drops to that.
    /// </summary>
    public bool QueueNext(ISampleProvider next, double overlapSeconds, Func<double>? currentRemainingSeconds)
    {
        if (next.WaveFormat.SampleRate != WaveFormat.SampleRate ||
            next.WaveFormat.Channels != WaveFormat.Channels ||
            next.WaveFormat.Encoding != WaveFormat.Encoding)
        {
            return false;
        }

        lock (_gate)
        {
            if (_crossfading)
                return false;
            _next = next;
            _overlapSeconds = Math.Max(0, overlapSeconds);
            _overlapFrames = (int)(_overlapSeconds * WaveFormat.SampleRate);
            _currentRemainingSeconds = currentRemainingSeconds;
            return true;
        }
    }

    /// <summary>Drops a queued next source. Does nothing once a crossfade is underway.</summary>
    public void Disarm()
    {
        lock (_gate)
        {
            if (_crossfading)
                return;
            _next = null;
            _currentRemainingSeconds = null;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        lock (_gate)
        {
            if (_next is null)
                return _current.Read(buffer, offset, count);

            if (!_crossfading && _overlapFrames > 0 && ShouldStartCrossfade())
            {
                _crossfading = true;
                _fadeFrame = 0;
                Transitioning?.Invoke();
            }

            return _crossfading
                ? ReadCrossfade(buffer, offset, count)
                : ReadWithSplice(buffer, offset, count);
        }
    }

    private bool ShouldStartCrossfade() =>
        _currentRemainingSeconds is { } remaining && remaining() <= _overlapSeconds;

    // Gapless: play current to its last sample, then keep filling the same buffer from next.
    private int ReadWithSplice(float[] buffer, int offset, int count)
    {
        var read = _current.Read(buffer, offset, count);
        if (read >= count)
            return read;

        Transitioning?.Invoke();
        _current = _next!;
        _next = null;
        _currentRemainingSeconds = null;
        OutgoingFinished?.Invoke();

        return read + _current.Read(buffer, offset + read, count - read);
    }

    private int ReadCrossfade(float[] buffer, int offset, int count)
    {
        var fromCurrent = _current.Read(buffer, offset, count);
        if (fromCurrent < count)
            Array.Clear(buffer, offset + fromCurrent, count - fromCurrent);

        if (_scratch.Length < count)
            _scratch = new float[count];
        var fromNext = _next!.Read(_scratch, 0, count);
        if (fromNext < count)
            Array.Clear(_scratch, fromNext, count - fromNext);

        var frames = count / _channels;
        for (var f = 0; f < frames; f++)
        {
            var p = Math.Min(1.0, (_fadeFrame + f) / (double)_overlapFrames);
            var angle = p * Math.PI / 2;
            var gainOut = (float)Math.Cos(angle);
            var gainIn = (float)Math.Sin(angle);
            var i = f * _channels;
            for (var c = 0; c < _channels; c++)
                buffer[offset + i + c] = (buffer[offset + i + c] * gainOut) + (_scratch[i + c] * gainIn);
        }

        _fadeFrame += frames;

        // Done when the fade completed, or the outgoing source ran dry (nothing left to blend).
        if (_fadeFrame >= _overlapFrames || fromCurrent == 0)
        {
            _current = _next!;
            _next = null;
            _currentRemainingSeconds = null;
            _crossfading = false;
            OutgoingFinished?.Invoke();
        }

        return Math.Max(fromCurrent, fromNext);
    }
}
