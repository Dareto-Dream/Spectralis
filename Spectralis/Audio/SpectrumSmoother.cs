using System;

namespace Spectralis.Audio
{
    public class SpectrumSmoother
    {
        private readonly float[] _prev;
        private readonly float _attackCoeff;
        private readonly float _releaseCoeff;

        public SpectrumSmoother(int bands, float attackMs = 5f, float releaseMs = 80f, float frameRateHz = 60f)
        {
            _prev = new float[bands];
            _attackCoeff = 1f - (float)Math.Exp(-1000f / (attackMs * frameRateHz));
            _releaseCoeff = 1f - (float)Math.Exp(-1000f / (releaseMs * frameRateHz));
        }

        public float[] Smooth(float[] input)
        {
            int len = Math.Min(input.Length, _prev.Length);
            var output = new float[len];

            for (int i = 0; i < len; i++)
            {
                float coeff = input[i] > _prev[i] ? _attackCoeff : _releaseCoeff;
                _prev[i] = _prev[i] + coeff * (input[i] - _prev[i]);
                output[i] = _prev[i];
            }

            return output;
        }

        public void Reset()
        {
            Array.Clear(_prev, 0, _prev.Length);
        }
    }
}
