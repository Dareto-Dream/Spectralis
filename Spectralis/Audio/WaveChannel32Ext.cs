using System;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public class FadeWaveProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private int _fadeSamplesTotal;
        private int _fadeSamplesRemaining;
        private bool _fadingIn;
        private bool _fadingOut;

        public FadeWaveProvider(ISampleProvider source)
        {
            _source = source;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public void BeginFadeIn(double fadeDurationMs)
        {
            _fadeSamplesTotal = (int)(WaveFormat.SampleRate * fadeDurationMs / 1000.0);
            _fadeSamplesRemaining = _fadeSamplesTotal;
            _fadingIn = true;
            _fadingOut = false;
        }

        public void BeginFadeOut(double fadeDurationMs)
        {
            _fadeSamplesTotal = (int)(WaveFormat.SampleRate * fadeDurationMs / 1000.0);
            _fadeSamplesRemaining = _fadeSamplesTotal;
            _fadingOut = true;
            _fadingIn = false;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            if (_fadingIn || _fadingOut)
            {
                for (int n = 0; n < read; n++)
                {
                    float multiplier = _fadingIn
                        ? 1f - (float)_fadeSamplesRemaining / _fadeSamplesTotal
                        : (float)_fadeSamplesRemaining / _fadeSamplesTotal;

                    buffer[offset + n] *= multiplier;

                    if (_fadeSamplesRemaining > 0)
                        _fadeSamplesRemaining--;
                    else
                    {
                        _fadingIn = _fadingOut = false;
                        break;
                    }
                }
            }

            return read;
        }
    }
}
