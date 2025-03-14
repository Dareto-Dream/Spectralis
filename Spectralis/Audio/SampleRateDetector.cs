using System;
using System.IO;

namespace Spectralis.Audio
{
    public static class SampleRateDetector
    {
        private static readonly int[] CommonRates = { 8000, 11025, 16000, 22050, 32000, 44100, 48000, 88200, 96000, 176400, 192000 };

        public static int DetectFromFile(string filePath)
        {
            try
            {
                var file = TagLib.File.Create(filePath);
                int rate = file.Properties.AudioSampleRate;
                return NearestStandard(rate);
            }
            catch
            {
                return 44100;
            }
        }

        public static bool IsHighRes(int sampleRate) => sampleRate > 48000;

        public static bool IsDsdRate(int sampleRate) => sampleRate == 2822400 || sampleRate == 5644800;

        private static int NearestStandard(int rate)
        {
            if (rate <= 0) return 44100;
            int nearest = CommonRates[0];
            int minDiff = Math.Abs(rate - nearest);
            foreach (var r in CommonRates)
            {
                int diff = Math.Abs(rate - r);
                if (diff < minDiff) { minDiff = diff; nearest = r; }
            }
            return minDiff < 2000 ? nearest : rate;
        }
    }
}
