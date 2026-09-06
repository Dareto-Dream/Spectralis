using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// Multi-band parametric equalizer. Replaces the fixed 10-band graphic EQ: each
/// band has an independent frequency, gain, Q and filter type, and the band count
/// is variable (<see cref="MinBands"/>–<see cref="MaxBands"/>). Defaults to the
/// legacy ISO 10-band layout at unity gain, so an existing rack is unchanged.
///
/// State is stored in the flat <see cref="EffectParameters"/> dictionary (keeping
/// the persistence / hot-swap seam), and the <see cref="EqSampleProvider"/>
/// re-reads it per block when <see cref="EffectParameters.Revision"/> changes —
/// so dragging a band updates the sound without tearing down the audio device.
/// </summary>
public sealed class ParametricEqEffect : IAudioEffect
{
    public const int MinBands = 3;
    public const int MaxBands = 16;

    /// <summary>Default band centre frequencies — the ISO 10-band set the legacy EQ used.</summary>
    public static readonly float[] DefaultFrequencies =
        [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public string Name => "Parametric EQ";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    public float PreampDb
    {
        get => Parameters.Get("preamp", 0f);
        set => Parameters.Set("preamp", Math.Clamp(value, -24f, 24f));
    }

    public float OutputGainDb
    {
        get => Parameters.Get("outGain", 0f);
        set => Parameters.Set("outGain", Math.Clamp(value, -24f, 24f));
    }

    public int BandCount =>
        Math.Clamp((int)Parameters.Get("bandCount", DefaultFrequencies.Length), MinBands, MaxBands);

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("preamp", 0f);
        p.Set("outGain", 0f);
        p.Set("bandCount", DefaultFrequencies.Length);
        for (var i = 0; i < DefaultFrequencies.Length; i++)
        {
            WriteBand(p, i, new EqBand(DefaultFrequencies[i], 0f, 1.41f, EqFilterType.Peak));
        }

        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) => new EqSampleProvider(source, this);

    public IReadOnlyList<EqBand> ReadBands()
    {
        var n = BandCount;
        var list = new EqBand[n];
        for (var i = 0; i < n; i++)
        {
            list[i] = ReadBand(Parameters, i);
        }

        return list;
    }

    public void WriteBands(IReadOnlyList<EqBand> bands)
    {
        var n = Math.Clamp(bands.Count, MinBands, MaxBands);
        Parameters.Set("bandCount", n);
        for (var i = 0; i < n; i++)
        {
            WriteBand(Parameters, i, bands[i]);
        }
    }

    public void SetBand(int index, EqBand band)
    {
        if (index < 0 || index >= BandCount)
        {
            return;
        }

        WriteBand(Parameters, index, band);
    }

    public void LoadPreset(EqPreset preset)
    {
        PreampDb = preset.PreampDb;
        OutputGainDb = preset.OutputGainDb;
        if (preset.Bands.Count > 0)
        {
            WriteBands(preset.Bands);
        }
    }

    /// <summary>
    /// Combined magnitude response (dB) of every enabled band plus preamp / output gain,
    /// evaluated at <paramref name="hz"/>. Drives the response-curve ("envelope") display.
    /// </summary>
    public double ResponseDb(double hz, int sampleRate = 48000)
    {
        var sum = (double)PreampDb + OutputGainDb;
        foreach (var band in ReadBands())
        {
            if (!band.Enabled)
            {
                continue;
            }

            sum += Biquad.MagnitudeDb(band.Type, sampleRate, band.Frequency, band.Q, band.GainDb, hz);
        }

        return sum;
    }

    private static void WriteBand(EffectParameters p, int i, EqBand b)
    {
        p.Set($"b{i}.freq", b.Frequency);
        p.Set($"b{i}.gain", b.GainDb);
        p.Set($"b{i}.q", b.Q);
        p.Set($"b{i}.type", (int)b.Type);
        p.Set($"b{i}.on", b.Enabled ? 1f : 0f);
    }

    private static EqBand ReadBand(EffectParameters p, int i) =>
        new(
            Math.Clamp(p.Get($"b{i}.freq", 1000f), 10f, 24000f),
            Math.Clamp(p.Get($"b{i}.gain", 0f), -24f, 24f),
            Math.Clamp(p.Get($"b{i}.q", 1f), 0.05f, 18f),
            (EqFilterType)Math.Clamp((int)p.Get($"b{i}.type", 0f), 0, 5),
            p.Get($"b{i}.on", 1f) >= 0.5f);

    private sealed class EqSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly ParametricEqEffect _effect;
        private readonly EffectParameters _params;
        private readonly int _channels;
        private readonly int _sampleRate;

        private Biquad[][] _filters = [];
        private int _builtRevision = int.MinValue;
        private int _builtBandCount;
        private float _preampLin = 1f;
        private float _outGainLin = 1f;

        public EqSampleProvider(ISampleProvider source, ParametricEqEffect effect)
        {
            _source = source;
            _effect = effect;
            _params = effect.Parameters;
            _channels = Math.Max(1, source.WaveFormat.Channels);
            _sampleRate = source.WaveFormat.SampleRate;
            Rebuild();
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            if (_params.Revision != _builtRevision)
            {
                Rebuild();
            }

            for (var i = 0; i < read; i += _channels)
            {
                for (var c = 0; c < _channels; c++)
                {
                    var s = buffer[offset + i + c] * _preampLin;
                    var chain = _filters[c];
                    for (var b = 0; b < chain.Length; b++)
                    {
                        s = chain[b].Process(s);
                    }

                    buffer[offset + i + c] = Math.Clamp(s * _outGainLin, -1f, 1f);
                }
            }

            return read;
        }

        private void Rebuild()
        {
            var bands = _effect.ReadBands();
            _builtBandCount = bands.Count;

            if (_filters.Length != _channels || _filters[0].Length != _builtBandCount)
            {
                _filters = new Biquad[_channels][];
                for (var c = 0; c < _channels; c++)
                {
                    _filters[c] = new Biquad[_builtBandCount];
                    for (var b = 0; b < _builtBandCount; b++)
                    {
                        _filters[c][b] = new Biquad();
                    }
                }
            }

            for (var b = 0; b < _builtBandCount; b++)
            {
                var band = bands[b];
                var gain = band.Enabled ? band.GainDb : 0f;
                var type = band.Enabled ? band.Type : EqFilterType.Peak;
                for (var c = 0; c < _channels; c++)
                {
                    _filters[c][b].SetCoefficients(type, _sampleRate, band.Frequency, band.Q, gain);
                }
            }

            _preampLin = (float)Math.Pow(10, _params.Get("preamp", 0f) / 20.0);
            _outGainLin = (float)Math.Pow(10, _params.Get("outGain", 0f) / 20.0);
            _builtRevision = _params.Revision;
        }
    }
}
