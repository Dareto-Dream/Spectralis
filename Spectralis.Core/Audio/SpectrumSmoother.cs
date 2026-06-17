namespace Spectralis.Core.Audio
{
    public class SpectrumSmoother
    {
        private readonly float[] _prev;
        private readonly float _attackCoeff;
        private readonly float _releaseCoeff;

        public SpectrumSmoother(int bandCount, float attackCoeff = 0.8f, float releaseCoeff = 0.12f)
        {
            _prev = new float[bandCount];
            _attackCoeff = attackCoeff;
            _releaseCoeff = releaseCoeff;
        }

        public float[] Smooth(float[] input)
        {
            var output = new float[input.Length];
            for (int i = 0; i < input.Length; i++)
            {
                float coeff = input[i] > _prev[i] ? _attackCoeff : _releaseCoeff;
                output[i] = _prev[i] = _prev[i] * coeff + input[i] * (1f - coeff);
            }
            return output;
        }

        public void Reset()
        {
            for (int i = 0; i < _prev.Length; i++) _prev[i] = 0;
        }
    }
}
