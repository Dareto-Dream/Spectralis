using System;

namespace Spectralis.Core.Visualizers
{
    public static class VisualizerContextHelper
    {
        public static float NormalizeEnergy(float raw, float floor = 0.02f, float ceiling = 0.85f)
        {
            if (raw <= floor) return 0f;
            return Math.Clamp((raw - floor) / (ceiling - floor), 0f, 1f);
        }

        public static float SmoothToward(float current, float target, float rate)
        {
            return current + (target - current) * Math.Clamp(rate, 0f, 1f);
        }

        public static float[] ComputeBandEnergies(float[] spectrum, int bandCount)
        {
            var result = new float[bandCount];
            if (spectrum.Length == 0) return result;

            for (int b = 0; b < bandCount; b++)
            {
                int start = b * spectrum.Length / bandCount;
                int end = Math.Min((b + 1) * spectrum.Length / bandCount, spectrum.Length);
                if (end <= start) { result[b] = 0f; continue; }
                float sum = 0f;
                for (int i = start; i < end; i++) sum += spectrum[i];
                result[b] = sum / (end - start);
            }
            return result;
        }

        public static float BassEnergy(float[] spectrum)
        {
            if (spectrum.Length == 0) return 0f;
            int end = Math.Min(spectrum.Length / 8, spectrum.Length);
            float sum = 0f;
            for (int i = 0; i < end; i++) sum += spectrum[i];
            return end > 0 ? sum / end : 0f;
        }

        public static float MidEnergy(float[] spectrum)
        {
            if (spectrum.Length == 0) return 0f;
            int start = spectrum.Length / 8;
            int end = spectrum.Length / 2;
            float sum = 0f;
            for (int i = start; i < end; i++) sum += spectrum[i];
            int count = end - start;
            return count > 0 ? sum / count : 0f;
        }

        public static float HighEnergy(float[] spectrum)
        {
            if (spectrum.Length == 0) return 0f;
            int start = spectrum.Length / 2;
            float sum = 0f;
            for (int i = start; i < spectrum.Length; i++) sum += spectrum[i];
            int count = spectrum.Length - start;
            return count > 0 ? sum / count : 0f;
        }
    }
}
