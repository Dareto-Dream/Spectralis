using System;

namespace Spectralis.Audio
{
    public static class VolumeNormalizer
    {
        private const float TargetLufs = -14f;

        public static float ComputeGain(float measuredLufs)
        {
            float diffDb = TargetLufs - measuredLufs;
            return (float)Math.Pow(10.0, diffDb / 20.0);
        }

        public static float DbToLinear(float db) => (float)Math.Pow(10.0, db / 20.0);

        public static float LinearToDb(float linear)
        {
            if (linear <= 0) return float.NegativeInfinity;
            return 20f * (float)Math.Log10(linear);
        }

        public static float Clamp(float gain, float minDb = -12f, float maxDb = 12f)
        {
            float minLinear = DbToLinear(minDb);
            float maxLinear = DbToLinear(maxDb);
            return Math.Max(minLinear, Math.Min(maxLinear, gain));
        }
    }
}
