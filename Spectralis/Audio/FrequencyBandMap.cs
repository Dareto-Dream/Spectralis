using System;

namespace Spectralis.Audio
{
    public static class FrequencyBandMap
    {
        public static readonly (string Name, float Low, float High)[] Bands = new[]
        {
            ("Sub-bass",   20f,   60f),
            ("Bass",       60f,  250f),
            ("Low-mid",   250f,  500f),
            ("Mid",       500f, 2000f),
            ("High-mid", 2000f, 4000f),
            ("Presence", 4000f, 6000f),
            ("Brilliance",6000f,20000f)
        };

        public static float GetBandEnergy(float[] spectrum, int sampleRate, int fftSize, int bandIndex)
        {
            if (bandIndex < 0 || bandIndex >= Bands.Length)
                return 0f;

            var (_, low, high) = Bands[bandIndex];
            float binWidth = (float)sampleRate / fftSize;
            int binLow = Math.Max(0, (int)(low / binWidth));
            int binHigh = Math.Min(spectrum.Length - 1, (int)(high / binWidth));

            if (binLow > binHigh) return 0f;

            float sum = 0;
            for (int i = binLow; i <= binHigh; i++)
                sum += spectrum[i];
            return sum / (binHigh - binLow + 1);
        }
    }
}
