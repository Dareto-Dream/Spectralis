using System;
using NAudio.Dsp;

namespace Spectralis.Core.Audio
{
    public class FftProcessor
    {
        private readonly int _fftSize;
        private readonly int _bandCount;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _window;
        private int _writePos;

        public FftProcessor(int fftSize = 2048, int bandCount = 64)
        {
            _fftSize = fftSize;
            _bandCount = bandCount;
            _fftBuffer = new Complex[fftSize];
            _window = new float[fftSize];

            for (int i = 0; i < fftSize; i++)
                _window[i] = (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        }

        public void Add(float sample)
        {
            _fftBuffer[_writePos].X = sample * _window[_writePos];
            _fftBuffer[_writePos].Y = 0;
            _writePos = (_writePos + 1) % _fftSize;
        }

        public float[] ComputeBands()
        {
            var buf = (Complex[])_fftBuffer.Clone();
            FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2), buf);

            float[] bands = new float[_bandCount];
            int halfSize = _fftSize / 2;

            double logMin = Math.Log10(20);
            double logMax = Math.Log10(20000);

            for (int b = 0; b < _bandCount; b++)
            {
                double logLo = logMin + (logMax - logMin) * b / _bandCount;
                double logHi = logMin + (logMax - logMin) * (b + 1) / _bandCount;

                int binLo = Math.Max(1, (int)(Math.Pow(10, logLo) * _fftSize / 44100));
                int binHi = Math.Min(halfSize - 1, (int)(Math.Pow(10, logHi) * _fftSize / 44100));

                float max = 0;
                for (int bin = binLo; bin <= binHi; bin++)
                {
                    float mag = (float)Math.Sqrt(buf[bin].X * buf[bin].X + buf[bin].Y * buf[bin].Y);
                    if (mag > max) max = mag;
                }
                bands[b] = max;
            }

            return bands;
        }
    }
}
