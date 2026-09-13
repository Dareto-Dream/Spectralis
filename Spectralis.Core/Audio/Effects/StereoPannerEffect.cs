using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>One draggable node on the pan-automation loop: a normalized loop position and a pan value.</summary>
public readonly record struct PanPoint(float Time, float Pan);

/// <summary>
/// Spatial positioning: downmixes to mono and repositions it in the stereo field
/// with equal-power pan gain plus a small interaural-time-difference (ITD) delay
/// between channels for a more "binaural" sense of direction than a plain gain pan.
/// Relevant for placing a capsule/world sound at a location.
///
/// Optionally, instead of a fixed <c>pan</c> value, the position can follow a
/// looping automation curve ("bar-based panning") — a handful of (time, pan) nodes
/// spanning <see cref="Bars"/> bars at <see cref="Bpm"/>, looping for the length of
/// playback. The loop restarts whenever the effect chain is rebuilt (track start,
/// seek, or any other rack edit), so it isn't locked to the track's actual downbeat —
/// good enough for "ear candy" movement without needing beat-grid plumbing into the
/// engine.
/// </summary>
public sealed class StereoPannerEffect : IAudioEffect
{
    public const int MinPoints = 2;
    public const int MaxPoints = 16;

    /// <summary>Default auto-pan loop: a smooth left/right/left sweep with no jump at the loop seam.</summary>
    public static readonly PanPoint[] DefaultPoints =
    [
        new(0f, 0f),
        new(0.25f, 0.8f),
        new(0.5f, 0f),
        new(0.75f, -0.8f),
    ];

    public string Name => "Stereo Panner";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    public bool AutoPanEnabled
    {
        get => Parameters.Get("autoPan", 0f) >= 0.5f;
        set => Parameters.Set("autoPan", value ? 1f : 0f);
    }

    public int Bars
    {
        get => Math.Clamp((int)Parameters.Get("bars", 4f), 1, 16);
        set => Parameters.Set("bars", Math.Clamp(value, 1, 16));
    }

    public float Bpm
    {
        get => Math.Clamp(Parameters.Get("bpm", 120f), 40f, 220f);
        set => Parameters.Set("bpm", Math.Clamp(value, 40f, 220f));
    }

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("pan", 0f);      // -1 (left) .. 1 (right) — used when autoPan is off
        p.Set("spread", 0.4f); // 0-1, strength of the ITD cue
        p.Set("autoPan", 0f);
        p.Set("bars", 4f);
        p.Set("bpm", 120f);
        p.Set("pointCount", DefaultPoints.Length);
        for (var i = 0; i < DefaultPoints.Length; i++)
        {
            WritePointRaw(p, i, DefaultPoints[i]);
        }

        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new PannerSampleProvider(source, Parameters);

    /// <summary>Points in stored (index) order — not necessarily sorted by time.</summary>
    public IReadOnlyList<PanPoint> ReadPoints()
    {
        var n = Math.Clamp((int)Parameters.Get("pointCount", DefaultPoints.Length), MinPoints, MaxPoints);
        var list = new PanPoint[n];
        for (var i = 0; i < n; i++)
        {
            list[i] = ReadPointRaw(Parameters, i);
        }

        return list;
    }

    /// <summary>Overwrites the whole point set, preserving the given order/indices (no re-sort).</summary>
    public void WritePoints(IReadOnlyList<PanPoint> points)
    {
        var n = Math.Clamp(points.Count, MinPoints, MaxPoints);
        Parameters.Set("pointCount", n);
        for (var i = 0; i < n; i++)
        {
            WritePointRaw(Parameters, i, points[i]);
        }
    }

    public void SetPoint(int index, PanPoint point)
    {
        if (index < 0 || index >= ReadPoints().Count)
        {
            return;
        }

        WritePointRaw(Parameters, index, point);
    }

    /// <summary>
    /// Interpolated pan (-1..1) at a normalized loop <paramref name="phase"/> (0-1, wraps).
    /// Points don't need to be pre-sorted — this sorts a local copy by time.
    /// </summary>
    public static float EvaluatePan(IReadOnlyList<PanPoint> points, float phase)
    {
        if (points.Count == 0)
        {
            return 0f;
        }

        if (points.Count == 1)
        {
            return points[0].Pan;
        }

        var sorted = points.OrderBy(p => p.Time).ToArray();
        phase = ((phase % 1f) + 1f) % 1f;

        if (phase < sorted[0].Time)
        {
            var prev = sorted[^1];
            var next = sorted[0];
            var span = (next.Time + 1f) - prev.Time;
            var frac = span > 1e-6f ? (phase + 1f - prev.Time) / span : 0f;
            return prev.Pan + ((next.Pan - prev.Pan) * frac);
        }

        for (var i = 0; i < sorted.Length - 1; i++)
        {
            if (phase >= sorted[i].Time && phase <= sorted[i + 1].Time)
            {
                var span = sorted[i + 1].Time - sorted[i].Time;
                var frac = span > 1e-6f ? (phase - sorted[i].Time) / span : 0f;
                return sorted[i].Pan + ((sorted[i + 1].Pan - sorted[i].Pan) * frac);
            }
        }

        var last = sorted[^1];
        var first = sorted[0];
        var wrapSpan = (first.Time + 1f) - last.Time;
        var wrapFrac = wrapSpan > 1e-6f ? (phase - last.Time) / wrapSpan : 0f;
        return last.Pan + ((first.Pan - last.Pan) * wrapFrac);
    }

    private static PanPoint ReadPointRaw(EffectParameters p, int i) =>
        new(
            Math.Clamp(p.Get($"p{i}.time", 0f), 0f, 1f),
            Math.Clamp(p.Get($"p{i}.pan", 0f), -1f, 1f));

    private static void WritePointRaw(EffectParameters p, int i, PanPoint point)
    {
        p.Set($"p{i}.time", Math.Clamp(point.Time, 0f, 1f));
        p.Set($"p{i}.pan", Math.Clamp(point.Pan, -1f, 1f));
    }

    private sealed class PannerSampleProvider : ISampleProvider
    {
        private const float MaxItdMs = 0.6f;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;
        private readonly float[] _lineL;
        private readonly float[] _lineR;
        private readonly int _bufferLength;
        private int _writePos;
        private double _elapsedSamples;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public PannerSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            _bufferLength = (int)(MaxItdMs / 1000.0 * source.WaveFormat.SampleRate) + 4;
            _lineL = new float[_bufferLength];
            _lineR = new float[_bufferLength];
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            var channels = _source.WaveFormat.Channels;
            if (channels < 2)
            {
                return read;
            }

            var staticPan = Math.Clamp(_params.Get("pan", 0f), -1f, 1f);
            var spread = Math.Clamp(_params.Get("spread", 0.4f), 0f, 1f);
            var autoPan = _params.Get("autoPan", 0f) >= 0.5f;
            var sampleRate = _source.WaveFormat.SampleRate;

            IReadOnlyList<PanPoint> points = [];
            double loopSeconds = 1;
            if (autoPan)
            {
                points = StereoPannerEffect.ReadPointsFrom(_params);
                var bars = Math.Clamp((int)_params.Get("bars", 4f), 1, 16);
                var bpm = Math.Clamp(_params.Get("bpm", 120f), 40f, 220f);
                loopSeconds = Math.Max(0.05, bars * 4.0 * (60.0 / bpm));
            }

            for (var i = 0; i < read; i += channels)
            {
                float pan;
                if (autoPan)
                {
                    var elapsedSeconds = _elapsedSamples / sampleRate;
                    var phase = (float)((elapsedSeconds % loopSeconds) / loopSeconds);
                    pan = EvaluatePan(points, phase);
                }
                else
                {
                    pan = staticPan;
                }

                var angle = (pan + 1f) * 0.25 * Math.PI;
                var gainL = (float)Math.Cos(angle);
                var gainR = (float)Math.Sin(angle);

                var itdSamples = (int)(Math.Abs(pan) * spread * MaxItdMs / 1000.0 * sampleRate);
                itdSamples = Math.Min(_bufferLength - 1, itdSamples);

                var mono = 0f;
                for (var c = 0; c < channels; c++)
                {
                    mono += buffer[offset + i + c];
                }

                mono /= channels;

                _lineL[_writePos] = mono * gainL;
                _lineR[_writePos] = mono * gainR;

                // Positive pan (toward the right) delays the left channel slightly so
                // the right arrives first; negative pan delays the right channel.
                var delayLeft = pan > 0 ? itdSamples : 0;
                var delayRight = pan < 0 ? itdSamples : 0;

                var posL = _writePos - delayLeft;
                if (posL < 0) posL += _bufferLength;
                var posR = _writePos - delayRight;
                if (posR < 0) posR += _bufferLength;

                buffer[offset + i] = Math.Clamp(_lineL[posL], -1f, 1f);
                buffer[offset + i + 1] = Math.Clamp(_lineR[posR], -1f, 1f);

                _writePos = (_writePos + 1) % _bufferLength;
                _elapsedSamples++;
            }

            return read;
        }
    }

    private static IReadOnlyList<PanPoint> ReadPointsFrom(EffectParameters p)
    {
        var n = Math.Clamp((int)p.Get("pointCount", DefaultPoints.Length), MinPoints, MaxPoints);
        var list = new PanPoint[n];
        for (var i = 0; i < n; i++)
        {
            list[i] = ReadPointRaw(p, i);
        }

        return list;
    }
}
