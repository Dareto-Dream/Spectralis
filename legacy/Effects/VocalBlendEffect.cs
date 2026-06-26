using NAudio.Wave;

namespace Spectralis;

internal sealed class VocalBlendEffect : IAudioEffect
{
    public string           Name       => "Vocal Remover";
    public bool             Enabled    { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("blend", 0.5f);  // 0=original, 1=side-only (vocals removed)
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new VocalBlendSampleProvider(source, Parameters);

    private sealed class VocalBlendSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public VocalBlendSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read     = _source.Read(buffer, offset, count);
            var channels = _source.WaveFormat.Channels;

            if (channels < 2) return read;  // only works on stereo

            var blend = Math.Clamp(_params.Get("blend", 0.5f), 0f, 1f);
            if (blend <= 0f) return read;

            for (var i = 0; i < read; i += channels)
            {
                var l = buffer[offset + i];
                var r = buffer[offset + i + 1];

                var mid  = (l + r) * 0.5f;
                var sideL = (l - r) * 0.5f;
                var sideR = (r - l) * 0.5f;

                var outL = mid * (1f - blend) + sideL;
                var outR = mid * (1f - blend) + sideR;

                buffer[offset + i]     = Math.Clamp(outL, -1f, 1f);
                buffer[offset + i + 1] = Math.Clamp(outR, -1f, 1f);
            }

            return read;
        }
    }
}
