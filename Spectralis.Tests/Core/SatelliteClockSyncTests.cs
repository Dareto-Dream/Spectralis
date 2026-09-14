using Spectralis.Core.Satellite;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class SatelliteClockSyncTests
{
    /// <summary>Builds a synthetic round trip for a receiver whose clock reads
    /// <paramref name="trueOffsetMs"/> behind the source's, with a symmetric one-way network
    /// delay and a given source-side processing delay between receiving the ping and sending
    /// the pong.</summary>
    private static ClockRoundTrip SimulateRoundTrip(double t0, double trueOffsetMs, double oneWayDelayMs, double processingDelayMs)
    {
        var t1 = t0 + oneWayDelayMs + trueOffsetMs;
        var t2 = t1 + processingDelayMs;
        var t3 = t2 + oneWayDelayMs - trueOffsetMs;
        return new ClockRoundTrip(t0, t1, t2, t3);
    }

    [Fact]
    public void ClockRoundTrip_RecoversExactOffsetAndDelay_WithSymmetricNetworkDelay()
    {
        var trip = SimulateRoundTrip(t0: 1000, trueOffsetMs: 42.5, oneWayDelayMs: 15, processingDelayMs: 3);

        Assert.Equal(42.5, trip.OffsetMs, precision: 6);
        Assert.Equal(30.0, trip.RoundTripDelayMs, precision: 6); // 2 * 15, processing time excluded
    }

    [Fact]
    public void ClockRoundTrip_NegativeOffset_WhenReceiverClockIsAhead()
    {
        var trip = SimulateRoundTrip(t0: 5000, trueOffsetMs: -100, oneWayDelayMs: 5, processingDelayMs: 1);

        Assert.Equal(-100.0, trip.OffsetMs, precision: 6);
    }

    [Fact]
    public void Estimator_NoSamples_ReturnsNull()
    {
        var estimator = new SatelliteClockSyncEstimator();

        Assert.Null(estimator.OffsetMs);
        Assert.Null(estimator.RoundTripDelayMs);
        Assert.Equal(0, estimator.SampleCount);
    }

    [Fact]
    public void Estimator_SingleSample_ReportsItsOwnOffset()
    {
        var estimator = new SatelliteClockSyncEstimator();
        estimator.AddSample(SimulateRoundTrip(0, trueOffsetMs: 10, oneWayDelayMs: 5, processingDelayMs: 0));

        Assert.Equal(10.0, estimator.OffsetMs!.Value, precision: 6);
        Assert.Equal(1, estimator.SampleCount);
    }

    [Fact]
    public void Estimator_PicksTheLowestDelaySample_NotTheMostRecent()
    {
        var estimator = new SatelliteClockSyncEstimator();
        // A noisy, high-delay sample first...
        estimator.AddSample(SimulateRoundTrip(0, trueOffsetMs: 999, oneWayDelayMs: 200, processingDelayMs: 0));
        // ...then a clean, low-delay one added after it. The clean one should win even though
        // it's not the only sample and even though it wasn't added first.
        estimator.AddSample(SimulateRoundTrip(1000, trueOffsetMs: 12, oneWayDelayMs: 2, processingDelayMs: 0));

        Assert.Equal(12.0, estimator.OffsetMs!.Value, precision: 6);
        Assert.Equal(4.0, estimator.RoundTripDelayMs!.Value, precision: 6);
    }

    [Fact]
    public void Estimator_WindowSlides_OldSamplesAgeOut()
    {
        var estimator = new SatelliteClockSyncEstimator(windowSize: 2);
        // This clean sample should fall out of the window once two more samples are added.
        estimator.AddSample(SimulateRoundTrip(0, trueOffsetMs: 1, oneWayDelayMs: 1, processingDelayMs: 0));
        estimator.AddSample(SimulateRoundTrip(1000, trueOffsetMs: 500, oneWayDelayMs: 50, processingDelayMs: 0));
        estimator.AddSample(SimulateRoundTrip(2000, trueOffsetMs: 700, oneWayDelayMs: 60, processingDelayMs: 0));

        Assert.Equal(2, estimator.SampleCount);
        // The offset=1 sample is gone; among the remaining two, offset=500 has the lower delay.
        Assert.Equal(500.0, estimator.OffsetMs!.Value, precision: 6);
    }

    [Fact]
    public void Estimator_RejectsNegativeRoundTripDelay()
    {
        var estimator = new SatelliteClockSyncEstimator();
        // A physically impossible sample (e.g. clock jumped backwards mid-measurement).
        estimator.AddSample(new ClockRoundTrip(T0: 1000, T1: 999, T2: 999, T3: 999));

        Assert.Equal(0, estimator.SampleCount);
        Assert.Null(estimator.OffsetMs);
    }

    [Fact]
    public void Estimator_Reset_ClearsAllSamples()
    {
        var estimator = new SatelliteClockSyncEstimator();
        estimator.AddSample(SimulateRoundTrip(0, trueOffsetMs: 10, oneWayDelayMs: 5, processingDelayMs: 0));

        estimator.Reset();

        Assert.Equal(0, estimator.SampleCount);
        Assert.Null(estimator.OffsetMs);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveWindowSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SatelliteClockSyncEstimator(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SatelliteClockSyncEstimator(-1));
    }
}
