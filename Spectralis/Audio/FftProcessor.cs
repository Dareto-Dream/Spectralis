using System;
using NAudio.Dsp;

namespace Spectralis.Audio
{
    public class FftProcessor
    {
        private readonly int _fftSize;
        private readonly Complex[] _fftBuffer;
        private readonly float[] _window;
        private int _sampleCount;

        public int BandCount { get; }
        public float[] Bands { get; }

        public FftProcessor(int fftSize = 2048, int bandCount = 64)
        {
            _fftSize = fftSize;
            BandCount = bandCount;
            Bands = new float[bandCount];
            _fftBuffer = new Complex[fftSize];
            _window = new float[fftSize];
            for (int i = 0; i < fftSize; i++)
                _window[i] = (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (fftSize - 1)));
        }

        public void Push(float[] samples, int offset, int count)
        {
            for (int i = 0; i < count && _sampleCount < _fftSize; i++, _sampleCount++)
            {
                _fftBuffer[_sampleCount].X = samples[offset + i] * _window[_sampleCount];
                _fftBuffer[_sampleCount].Y = 0;
            }

            if (_sampleCount >= _fftSize)
            {
                FastFourierTransform.FFT(true, (int)Math.Log(_fftSize, 2), _fftBuffer);
                ComputeBands();
                _sampleCount = 0;
            }
        }

        private void ComputeBands()
        {
            int half = _fftSize / 2;
            float logMin = (float)Math.Log10(20.0);
            float logMax = (float)Math.Log10(20000.0);

            for (int b = 0; b < BandCount; b++)
            {
                float freqLow = (float)Math.Pow(10, logMin + (logMax - logMin) * b / BandCount);
                float freqHigh = (float)Math.Pow(10, logMin + (logMax - logMin) * (b + 1) / BandCount);

                int binLow = Math.Max(0, (int)(freqLow * _fftSize / 44100f));
                int binHigh = Math.Min(half - 1, (int)(freqHigh * _fftSize / 44100f));

                float sum = 0f;
                int cnt = 0;
                for (int bin = binLow; bin <= binHigh; bin++)
                {
                    float re = _fftBuffer[bin].X;
                    float im = _fftBuffer[bin].Y;
                    sum += (float)Math.Sqrt(re * re + im * im);
                    cnt++;
                }

                Bands[b] = cnt > 0 ? sum / cnt : 0f;
            }
        }

        public void Reset()
        {
            _sampleCount = 0;
            Array.Clear(Bands, 0, Bands.Length);
        }
    }
}
