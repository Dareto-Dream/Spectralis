using System.Diagnostics;

namespace Spectralis.Core.Satellite;

/// <summary>High-resolution monotonic clock shared by both source and receiver sides. Only
/// deltas within one side's own readings matter for <see cref="ClockRoundTrip"/> — the two
/// machines' clocks are never compared directly, so this doesn't need to be wall-clock/NTP
/// synced itself.</summary>
public static class SatelliteClock
{
    public static double NowMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;
}
