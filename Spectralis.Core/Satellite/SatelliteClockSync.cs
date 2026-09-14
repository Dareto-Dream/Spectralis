namespace Spectralis.Core.Satellite;

/// <summary>
/// One NTP-style four-timestamp round trip:
/// <list type="bullet">
/// <item>t0 — receiver's clock when it sent the ping</item>
/// <item>t1 — source's clock when it received the ping</item>
/// <item>t2 — source's clock when it sent the pong</item>
/// <item>t3 — receiver's clock when it received the pong</item>
/// </list>
/// All four in the same unit (milliseconds); each side's own monotonic clock, not wall time —
/// only deltas within one side matter, so clock skew between machines doesn't need NTP/wall-clock
/// sync of its own.
/// </summary>
public readonly record struct ClockRoundTrip(double T0, double T1, double T2, double T3)
{
    /// <summary>
    /// Estimated one-way offset: add this to the receiver's clock to get the source's clock
    /// (i.e. <c>sourceTime ≈ receiverTime + Offset</c>). Standard NTP formula — assumes
    /// symmetric network delay in each direction, which is the best any deployment can assume
    /// without dedicated hardware timestamping.
    /// </summary>
    public double OffsetMs => ((T1 - T0) + (T2 - T3)) / 2.0;

    /// <summary>Total round-trip network delay, with the source's own processing time
    /// (T2 - T1) subtracted out.</summary>
    public double RoundTripDelayMs => (T3 - T0) - (T2 - T1);
}

/// <summary>
/// Keeps a rolling window of clock round trips and reports the offset from the sample with the
/// lowest round-trip delay — standard NTP practice, since delay asymmetry (the actual source of
/// offset error) correlates with how long the round trip took. A single noisy sample (e.g. a
/// GC pause or a wifi retransmit mid-flight) can't corrupt the estimate; it just gets outweighed
/// by better samples already in the window, or ages out.
/// </summary>
public sealed class SatelliteClockSyncEstimator
{
    private readonly int _windowSize;
    private readonly List<ClockRoundTrip> _samples = [];

    public SatelliteClockSyncEstimator(int windowSize = 8)
    {
        if (windowSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(windowSize), "windowSize must be at least 1");
        }

        _windowSize = windowSize;
    }

    public int SampleCount => _samples.Count;

    public void AddSample(ClockRoundTrip sample)
    {
        // Reject a physically impossible round trip (negative delay — clock jumped backwards
        // mid-measurement, e.g. system sleep/resume) rather than let it corrupt the window.
        if (sample.RoundTripDelayMs < 0)
        {
            return;
        }

        _samples.Add(sample);
        if (_samples.Count > _windowSize)
        {
            _samples.RemoveAt(0);
        }
    }

    /// <summary>Best current offset estimate, or null if no valid sample has been added yet.</summary>
    public double? OffsetMs => BestSample()?.OffsetMs;

    /// <summary>Round-trip delay of the sample the current offset estimate came from.</summary>
    public double? RoundTripDelayMs => BestSample()?.RoundTripDelayMs;

    public void Reset() => _samples.Clear();

    private ClockRoundTrip? BestSample()
    {
        if (_samples.Count == 0)
        {
            return null;
        }

        var best = _samples[0];
        for (var i = 1; i < _samples.Count; i++)
        {
            if (_samples[i].RoundTripDelayMs < best.RoundTripDelayMs)
            {
                best = _samples[i];
            }
        }

        return best;
    }
}
