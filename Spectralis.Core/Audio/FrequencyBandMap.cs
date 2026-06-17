using System.Collections.Generic;

namespace Spectralis.Core.Audio
{
    public class FrequencyBandMap
    {
        public record NamedBand(string Name, double LowHz, double HighHz);

        public static readonly IReadOnlyList<NamedBand> StandardBands = new[]
        {
            new NamedBand("Sub-bass",    20,   60),
            new NamedBand("Bass",        60,  250),
            new NamedBand("Low-mid",    250,  500),
            new NamedBand("Mid",        500, 2000),
            new NamedBand("Upper-mid", 2000, 4000),
            new NamedBand("Presence",  4000, 6000),
            new NamedBand("Brilliance",6000,20000)
        };

        public static float GetBandEnergy(float[] spectrum, double lowHz, double highHz, int sampleRate = 44100)
        {
            int fftSize = spectrum.Length * 2;
            double hzPerBin = sampleRate / (double)fftSize;
            int lo = (int)(lowHz / hzPerBin);
            int hi = (int)(highHz / hzPerBin);

            if (lo >= spectrum.Length) return 0;
            hi = hi < spectrum.Length ? hi : spectrum.Length - 1;

            float max = 0;
            for (int i = lo; i <= hi; i++)
                if (spectrum[i] > max) max = spectrum[i];
            return max;
        }
    }
}
