using System;
using System.Collections.Generic;

namespace Spectralis.Core.SharedPlay
{
    public class SyncDriftDetector
    {
        private const double DriftThresholdSeconds = 0.5;
        private const int SampleWindow = 10;
        private readonly Queue<double> _deltas = new();

        public bool IsDrifting { get; private set; }
        public double AverageDriftSeconds { get; private set; }

        public void Record(TimeSpan localPosition, TimeSpan remotePosition)
        {
            double delta = Math.Abs((localPosition - remotePosition).TotalSeconds);
            _deltas.Enqueue(delta);
            if (_deltas.Count > SampleWindow) _deltas.Dequeue();

            double sum = 0;
            foreach (var d in _deltas) sum += d;
            AverageDriftSeconds = sum / _deltas.Count;
            IsDrifting = AverageDriftSeconds > DriftThresholdSeconds;
        }

        public void Reset()
        {
            _deltas.Clear();
            IsDrifting = false;
            AverageDriftSeconds = 0;
        }
    }
}
