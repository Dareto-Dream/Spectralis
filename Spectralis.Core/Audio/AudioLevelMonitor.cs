using System;

namespace Spectralis.Core.Audio
{
    public class AudioLevelMonitor
    {
        private float _peakLeft;
        private float _peakRight;
        private int _holdFrames;
        private const int PeakHoldFrames = 60;

        public float RmsLeft { get; private set; }
        public float RmsRight { get; private set; }
        public float PeakLeft => _peakLeft;
        public float PeakRight => _peakRight;
        public bool IsClipping { get; private set; }

        public event EventHandler? ClipDetected;

        public void ProcessFrame(float[] interleaved, int channels)
        {
            if (channels == 0 || interleaved.Length == 0) return;

            double sumL = 0, sumR = 0;
            int count = interleaved.Length / channels;

            for (int i = 0; i < count; i++)
            {
                float l = interleaved[i * channels];
                float r = channels > 1 ? interleaved[i * channels + 1] : l;
                sumL += l * l;
                sumR += r * r;

                float absL = Math.Abs(l);
                float absR = Math.Abs(r);
                if (absL > _peakLeft) { _peakLeft = absL; _holdFrames = PeakHoldFrames; }
                if (absR > _peakRight) { _peakRight = absR; _holdFrames = PeakHoldFrames; }
            }

            RmsLeft = (float)Math.Sqrt(sumL / count);
            RmsRight = (float)Math.Sqrt(sumR / count);

            bool clipping = _peakLeft >= 1.0f || _peakRight >= 1.0f;
            if (clipping && !IsClipping) ClipDetected?.Invoke(this, EventArgs.Empty);
            IsClipping = clipping;

            if (--_holdFrames <= 0)
            {
                _peakLeft *= 0.97f;
                _peakRight *= 0.97f;
            }
        }
    }
}
