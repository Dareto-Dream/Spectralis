using System;
using System.Collections.Generic;

namespace Spectralis.Core.Analysis
{
    public class BeatMarker
    {
        public double TimeSeconds { get; init; }
        public int BeatNumber { get; init; }
        public bool IsDownbeat => BeatNumber % 4 == 0;
    }

    public class BeatGrid
    {
        public float Bpm { get; init; }
        public float Confidence { get; init; }
        public double GridStartSeconds { get; init; }
        public IReadOnlyList<BeatMarker> Beats { get; init; } = Array.Empty<BeatMarker>();
        public bool IsValid => Bpm >= 40f && Bpm <= 220f && Confidence >= 0.4f;

        public BeatMarker? GetNearestBeat(double positionSeconds, double toleranceSeconds = 0.05)
        {
            BeatMarker? nearest = null;
            double minDelta = double.MaxValue;

            foreach (var beat in Beats)
            {
                double delta = Math.Abs(beat.TimeSeconds - positionSeconds);
                if (delta < minDelta) { minDelta = delta; nearest = beat; }
            }
            return minDelta <= toleranceSeconds ? nearest : null;
        }

        public static BeatGrid Build(BpmResult bpm, double trackDurationSeconds, double firstBeatSeconds = 0.0)
        {
            if (!bpm.IsValid) return new BeatGrid();

            double beatInterval = 60.0 / bpm.Bpm;
            var beats = new List<BeatMarker>();
            int beatNum = 0;
            for (double t = firstBeatSeconds; t < trackDurationSeconds; t += beatInterval)
            {
                beats.Add(new BeatMarker { TimeSeconds = t, BeatNumber = beatNum++ });
            }
            return new BeatGrid
            {
                Bpm = bpm.Bpm,
                Confidence = bpm.Confidence,
                GridStartSeconds = firstBeatSeconds,
                Beats = beats
            };
        }
    }
}
