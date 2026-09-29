using System.Numerics;
using NAudio.Wave;

namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// True convolution reverb: processes audio against an impulse response (IR) via
/// uniformly-partitioned FFT convolution, rather than the comb/allpass network
/// <see cref="ReverbEffect"/> uses. Much richer tails, heavier CPU cost — the
/// classic tradeoff.
///
/// Real convolution reverbs load a recorded IR (a real space or hardware unit).
/// Loading user-supplied IR files would mean adding a file picker and an asset
/// store, so instead the IR is synthesized procedurally from <c>size</c>/<c>damping</c>
/// (sparse early-reflection taps + a damped noise tail) — same convolution engine,
/// no external assets required.
/// </summary>
public sealed class ConvolutionReverbEffect : IAudioEffect
{
    public string Name => "Convolution Reverb";
    public bool Enabled { get; set; } = true;
    public EffectParameters Parameters { get; } = BuildDefaultParams();

    private static EffectParameters BuildDefaultParams()
    {
        var p = new EffectParameters();
        p.Set("size", 0.5f);     // 0-1, IR length / room size
        p.Set("damping", 0.5f);  // 0-1, high-frequency absorption in the tail
        p.Set("mix", 0.35f);     // 0-1
        return p;
    }

    public ISampleProvider Wrap(ISampleProvider source) =>
        new ConvolutionSampleProvider(source, Parameters);

    /// <summary>Minimal iterative radix-2 Cooley-Tukey FFT over power-of-two buffers.</summary>
    private static class Fft
    {
        public static void Transform(Complex[] buffer, bool inverse)
        {
            var n = buffer.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;
                if (i < j)
                {
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
                }
            }

            for (var len = 2; len <= n; len <<= 1)
            {
                var ang = 2 * Math.PI / len * (inverse ? 1 : -1);
                var wlen = new Complex(Math.Cos(ang), Math.Sin(ang));
                for (var i = 0; i < n; i += len)
                {
                    var w = Complex.One;
                    for (var k = 0; k < len / 2; k++)
                    {
                        var u = buffer[i + k];
                        var v = buffer[i + k + (len / 2)] * w;
                        buffer[i + k] = u + v;
                        buffer[i + k + (len / 2)] = u - v;
                        w *= wlen;
                    }
                }
            }

            if (inverse)
            {
                for (var i = 0; i < n; i++)
                {
                    buffer[i] /= n;
                }
            }
        }
    }

    private sealed class ConvolutionSampleProvider : ISampleProvider
    {
        private const int BlockSize = 1024;
        private const int FftSize = BlockSize * 2;

        private readonly ISampleProvider _source;
        private readonly EffectParameters _params;

        private Complex[][] _irPartitions = [];
        private Complex[][] _history = [];
        private int _historyPos;
        private int _numPartitions;

        private readonly float[] _inputAccum = new float[BlockSize];
        private int _accumFill;
        private float[] _tailCarry = new float[BlockSize];
        private readonly Queue<float> _wetQueue = new();

        private float _builtSize = float.NaN;
        private float _builtDamping = float.NaN;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public ConvolutionSampleProvider(ISampleProvider source, EffectParameters parameters)
        {
            _source = source;
            _params = parameters;
            RebuildIr(source.WaveFormat.SampleRate);
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read == 0)
            {
                return 0;
            }

            var size = Math.Clamp(_params.Get("size", 0.5f), 0f, 1f);
            var damping = Math.Clamp(_params.Get("damping", 0.5f), 0f, 1f);
            if (Math.Abs(size - _builtSize) > 0.001f || Math.Abs(damping - _builtDamping) > 0.001f)
            {
                RebuildIr(_source.WaveFormat.SampleRate);
            }

            var mix = Math.Clamp(_params.Get("mix", 0.35f), 0f, 1f);
            var channels = Math.Max(1, _source.WaveFormat.Channels);

            for (var i = 0; i < read; i += channels)
            {
                var mono = 0f;
                for (var c = 0; c < channels; c++)
                {
                    mono += buffer[offset + i + c];
                }

                mono /= channels;

                _inputAccum[_accumFill++] = mono;
                if (_accumFill == BlockSize)
                {
                    ProcessBlock();
                    _accumFill = 0;
                }

                var wet = _wetQueue.Count > 0 ? _wetQueue.Dequeue() : 0f;
                for (var c = 0; c < channels; c++)
                {
                    var dry = buffer[offset + i + c];
                    buffer[offset + i + c] = Math.Clamp((dry * (1f - mix)) + (wet * mix), -1f, 1f);
                }
            }

            return read;
        }

        private void ProcessBlock()
        {
            var block = new Complex[FftSize];
            for (var i = 0; i < BlockSize; i++)
            {
                block[i] = new Complex(_inputAccum[i], 0);
            }

            Fft.Transform(block, false);
            _history[_historyPos] = block;

            var sum = new Complex[FftSize];
            for (var p = 0; p < _numPartitions; p++)
            {
                var histIndex = (_historyPos - p + _numPartitions) % _numPartitions;
                var hist = _history[histIndex];
                var ir = _irPartitions[p];
                for (var k = 0; k < FftSize; k++)
                {
                    sum[k] += hist[k] * ir[k];
                }
            }

            Fft.Transform(sum, true);

            for (var i = 0; i < BlockSize; i++)
            {
                _wetQueue.Enqueue((float)sum[i].Real + _tailCarry[i]);
            }

            for (var i = 0; i < BlockSize; i++)
            {
                _tailCarry[i] = (float)sum[BlockSize + i].Real;
            }

            _historyPos = (_historyPos + 1) % _numPartitions;
        }

        private void RebuildIr(int sampleRate)
        {
            var size = Math.Clamp(_params.Get("size", 0.5f), 0f, 1f);
            var damping = Math.Clamp(_params.Get("damping", 0.5f), 0f, 1f);

            var ir = GenerateImpulseResponse(sampleRate, size, damping);
            _numPartitions = Math.Max(1, (int)Math.Ceiling(ir.Length / (double)BlockSize));

            _irPartitions = new Complex[_numPartitions][];
            for (var p = 0; p < _numPartitions; p++)
            {
                var seg = new Complex[FftSize];
                var start = p * BlockSize;
                for (var i = 0; i < BlockSize; i++)
                {
                    var idx = start + i;
                    seg[i] = idx < ir.Length ? new Complex(ir[idx], 0) : Complex.Zero;
                }

                Fft.Transform(seg, false);
                _irPartitions[p] = seg;
            }

            _history = new Complex[_numPartitions][];
            for (var p = 0; p < _numPartitions; p++)
            {
                _history[p] = new Complex[FftSize];
            }

            _historyPos = 0;
            _tailCarry = new float[BlockSize];
            _wetQueue.Clear();
            _accumFill = 0;

            _builtSize = size;
            _builtDamping = damping;
        }

        /// <summary>
        /// Sparse early-reflection taps followed by a damped noise tail, deterministic
        /// (fixed seed) so the same size/damping always produces the same-sounding room.
        /// </summary>
        private static float[] GenerateImpulseResponse(int sampleRate, float size, float damping)
        {
            var seconds = 0.3f + (size * 2.2f);
            var length = Math.Max(BlockSize, (int)(seconds * sampleRate));
            var ir = new float[length];
            var rng = new Random(1337);

            const int earlyCount = 10;
            var earlySpanSamples = (int)((0.02 + (size * 0.06)) * sampleRate);
            for (var i = 0; i < earlyCount; i++)
            {
                var pos = (int)((double)(i + 1) / (earlyCount + 1) * earlySpanSamples) + rng.Next(-200, 200);
                pos = Math.Clamp(pos, 0, length - 1);
                var amp = (1f - ((float)i / earlyCount)) * 0.6f;
                ir[pos] += ((rng.NextSingle() * 2f) - 1f) * amp;
            }

            var tauSamples = length / 3.0;
            const float cutoffStart = 16000f;
            var cutoffEnd = 2000f + ((1f - damping) * 8000f);
            var lp = 0f;

            for (var n = 0; n < length; n++)
            {
                var t = (float)n / length;
                var cutoff = cutoffStart + ((cutoffEnd - cutoffStart) * t);
                var alpha = (float)(1.0 - Math.Exp(-2.0 * Math.PI * cutoff / sampleRate));
                var noise = (rng.NextSingle() * 2f) - 1f;
                lp += alpha * (noise - lp);

                var envelope = (float)Math.Exp(-n / tauSamples) * 0.5f;
                ir[n] += lp * envelope;
            }

            var peak = 0f;
            for (var n = 0; n < length; n++)
            {
                peak = Math.Max(peak, Math.Abs(ir[n]));
            }

            if (peak > 1e-6f)
            {
                var norm = 0.9f / peak;
                for (var n = 0; n < length; n++)
                {
                    ir[n] *= norm;
                }
            }

            return ir;
        }
    }
}
