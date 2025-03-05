using System;

namespace Spectralis.Audio
{
    public readonly struct AudioPosition
    {
        public TimeSpan Current { get; }
        public TimeSpan Total { get; }

        public AudioPosition(TimeSpan current, TimeSpan total)
        {
            Current = current;
            Total = total;
        }

        public double Fraction => Total > TimeSpan.Zero ? Current.TotalSeconds / Total.TotalSeconds : 0.0;

        public static readonly AudioPosition Zero = new AudioPosition(TimeSpan.Zero, TimeSpan.Zero);

        public override string ToString() => $"{FormatTime(Current)} / {FormatTime(Total)}";

        private static string FormatTime(TimeSpan t) =>
            t.Hours > 0 ? $"{t.Hours}:{t.Minutes:D2}:{t.Seconds:D2}" : $"{t.Minutes}:{t.Seconds:D2}";
    }
}
