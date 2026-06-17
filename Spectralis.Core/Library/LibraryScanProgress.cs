using System;

namespace Spectralis.Core.Library
{
    public class LibraryScanProgress
    {
        public int ScannedCount { get; set; }
        public int TotalEstimate { get; set; }
        public int ErrorCount { get; set; }
        public string CurrentFile { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
        public TimeSpan Elapsed { get; set; }

        public double ProgressFraction =>
            TotalEstimate > 0 ? Math.Clamp((double)ScannedCount / TotalEstimate, 0, 1) : 0;

        public string Summary =>
            IsComplete
                ? $"Scan complete — {ScannedCount} tracks ({ErrorCount} errors) in {Elapsed.TotalSeconds:0.0}s"
                : $"Scanning… {ScannedCount} / {TotalEstimate}";
    }
}
