using System;

namespace Spectralis.App.Visualizers
{
    public sealed class VisualizerScriptApi
    {
        public float[] spectrum { get; set; } = Array.Empty<float>();
        public float[] waveform { get; set; } = Array.Empty<float>();
        public float rms { get; set; }
        public bool beat { get; set; }

        public float lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0f, 1f);

        public float clamp(float v, float min, float max) => Math.Clamp(v, min, max);

        public float abs(float v) => Math.Abs(v);

        public float sin(float v) => (float)Math.Sin(v);

        public float cos(float v) => (float)Math.Cos(v);

        public float pow(float b, float e) => (float)Math.Pow(b, e);

        public float sqrt(float v) => (float)Math.Sqrt(Math.Max(0f, v));

        public float floor(float v) => (float)Math.Floor(v);

        public float ceil(float v) => (float)Math.Ceiling(v);

        public float getBand(int index, int totalBands)
        {
            if (spectrum.Length == 0 || totalBands <= 0) return 0f;
            int size = spectrum.Length / totalBands;
            if (size == 0) return 0f;
            int start = Math.Min(index * size, spectrum.Length - 1);
            int end = Math.Min(start + size, spectrum.Length);
            float sum = 0f;
            for (int i = start; i < end; i++) sum += spectrum[i];
            return sum / (end - start);
        }
    }
}
