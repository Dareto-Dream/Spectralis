using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Mid-side width control: scales the side (difference) signal to widen or narrow
/// the perceived stereo image without any true multi-mic recording.
/// </summary>
public sealed class StereoWidenerEffect : IAudioEffect
{
    public string Name => "Stereo Widener";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("width", 1.4f);  // 0=mono, 1=unchanged, 2=very wide
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new WidenerSampleProvider(source, Parameters);

    private sealed class WidenerSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public WidenerSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            var channels = _source.WaveFormat.Channels;
            if (channels < 2)
            {
                return read;
            }

            var width = Math.Clamp(_params.Get("width", 1.4f), 0f, 2f);

            for (var i = 0; i < read; i += channels)
            {
                var l = buffer[offset + i];
                var r = buffer[offset + i + 1];

                var mid = (l + r) * 0.5f;
                var side = (l - r) * 0.5f * width;

                buffer[offset + i] = Math.Clamp(mid + side, -1f, 1f);
                buffer[offset + i + 1] = Math.Clamp(mid - side, -1f, 1f);
            }

            return read;
        }
    }
}
