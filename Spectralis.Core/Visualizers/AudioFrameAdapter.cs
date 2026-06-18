using Spectralis.Core.Audio;

namespace Spectralis.Core.Visualizers
{
    public static class AudioFrameAdapter
    {
        public static float GetBandEnergy(in AudioFrame frame, int bandIndex, int totalBands)
        {
            if (frame.Spectrum.Length == 0 || totalBands <= 0) return 0f;
            int bandSize = frame.Spectrum.Length / totalBands;
            if (bandSize == 0) return frame.Spectrum[System.Math.Min(bandIndex, frame.Spectrum.Length - 1)];

            int start = bandIndex * bandSize;
            int end = System.Math.Min(start + bandSize, frame.Spectrum.Length);

            float energy = 0f;
            for (int i = start; i < end; i++) energy += frame.Spectrum[i];
            return energy / (end - start);
        }

        public static float GetLoudness(in AudioFrame frame) =>
            (frame.RmsLeft + frame.RmsRight) / 2f;

        public static float GetPeak(in AudioFrame frame) =>
            System.Math.Max(frame.PeakLeft, frame.PeakRight);

        public static float[] SubsampleSpectrum(float[] spectrum, int targetBands)
        {
            if (spectrum.Length == 0 || targetBands <= 0) return new float[targetBands];
            var result = new float[targetBands];
            float ratio = spectrum.Length / (float)targetBands;
            for (int i = 0; i < targetBands; i++)
            {
                int start = (int)(i * ratio);
                int end = System.Math.Min((int)((i + 1) * ratio), spectrum.Length);
                float sum = 0f;
                for (int j = start; j < end; j++) sum += spectrum[j];
                result[i] = end > start ? sum / (end - start) : 0f;
            }
            return result;
        }
    }
}
