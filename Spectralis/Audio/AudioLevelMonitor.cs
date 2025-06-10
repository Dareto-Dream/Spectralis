using System;

namespace Spectralis.Audio
{
    public class AudioLevelMonitor
    {
        private float _peakL;
        private float _peakR;
        private float _rmsL;
        private float _rmsR;
        private bool _clipL;
        private bool _clipR;
        private readonly float _decayRate;

        public float PeakL => _peakL;
        public float PeakR => _peakR;
        public float RmsL => _rmsL;
        public float RmsR => _rmsR;
        public bool ClipL => _clipL;
        public bool ClipR => _clipR;

        public event EventHandler ClipDetected;

        public AudioLevelMonitor(float decayRate = 0.05f)
        {
            _decayRate = decayRate;
        }

        public void ProcessFrame(float[] samples, int channels)
        {
            if (samples == null || samples.Length == 0) return;

            float sumSqL = 0, sumSqR = 0;
            float maxL = 0, maxR = 0;
            int frames = samples.Length / channels;

            for (int i = 0; i < frames; i++)
            {
                float l = samples[i * channels];
                float r = channels > 1 ? samples[i * channels + 1] : l;

                sumSqL += l * l;
                sumSqR += r * r;
                maxL = Math.Max(maxL, Math.Abs(l));
                maxR = Math.Max(maxR, Math.Abs(r));
            }

            _rmsL = (float)Math.Sqrt(sumSqL / frames);
            _rmsR = (float)Math.Sqrt(sumSqR / frames);
            _peakL = Math.Max(maxL, _peakL * (1f - _decayRate));
            _peakR = Math.Max(maxR, _peakR * (1f - _decayRate));

            bool newClipL = maxL >= 1.0f;
            bool newClipR = maxR >= 1.0f;

            if ((newClipL && !_clipL) || (newClipR && !_clipR))
                ClipDetected?.Invoke(this, EventArgs.Empty);

            _clipL = newClipL;
            _clipR = newClipR;
        }

        public void Reset()
        {
            _peakL = _peakR = _rmsL = _rmsR = 0;
            _clipL = _clipR = false;
        }
    }
}
