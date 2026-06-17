using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Spectralis.Core.Analysis
{
    public sealed class AnalysisWorker
    {
        private readonly BpmAnalyzer _bpm = new();
        private readonly KeyAnalyzer _key = new();

        public event EventHandler<AnalysisResult>? AnalysisCompleted;

        public async Task<AnalysisResult?> AnalyzeAsync(string filePath, CancellationToken ct = default)
        {
            if (!File.Exists(filePath)) return null;

            return await Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                float[] samples = ReadSamples(filePath, out int sampleRate);
                ct.ThrowIfCancellationRequested();

                var bpmResult = _bpm.Analyze(samples, sampleRate);
                var chromagram = ComputeChromagram(samples, sampleRate);
                var keyResult = _key.Analyze(chromagram);
                var beatGrid = BeatGrid.Build(bpmResult, samples.Length / (double)sampleRate);
                float lufs = MeasureLoudness(samples);

                var result = new AnalysisResult
                {
                    FilePath = filePath,
                    Bpm = bpmResult,
                    Key = keyResult,
                    BeatGrid = beatGrid,
                    LoudnessLufs = lufs
                };

                AnalysisCompleted?.Invoke(this, result);
                return result;
            }, ct);
        }

        private float[] ReadSamples(string path, out int sampleRate)
        {
            using var reader = new AudioFileReader(path);
            sampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;
            var buffer = new float[reader.WaveFormat.SampleRate * 30 * channels];
            int read = reader.Read(buffer, 0, buffer.Length);

            if (channels == 1) return buffer[..read];

            var mono = new float[read / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++) sum += buffer[i * channels + c];
                mono[i] = sum / channels;
            }
            return mono;
        }

        private float[] ComputeChromagram(float[] samples, int sampleRate, System.Threading.CancellationToken ct = default)
        {
            var chroma = new float[12];
            int windowSize = sampleRate / 8;
            int hopSize = windowSize * 2;
            int frames = 0;

            for (int i = 0; i + windowSize < samples.Length; i += hopSize)
            {
                ct.ThrowIfCancellationRequested();
                for (int note = 0; note < 12; note++)
                {
                    float freq = 261.63f * MathF.Pow(2f, note / 12f);
                    float energy = ComputeEnergyAtFreq(samples, i, windowSize, freq, sampleRate);
                    chroma[note] += energy;
                }
                frames++;
            }

            if (frames > 0)
                for (int n = 0; n < 12; n++) chroma[n] /= frames;

            return chroma;
        }

        private float ComputeEnergyAtFreq(float[] samples, int offset, int windowSize, float freq, int sampleRate)
        {
            float re = 0f, im = 0f;
            float w = 2f * MathF.PI * freq / sampleRate;
            int end = Math.Min(offset + windowSize, samples.Length);
            for (int i = offset; i < end; i++)
            {
                float phase = w * (i - offset);
                re += samples[i] * MathF.Cos(phase);
                im += samples[i] * MathF.Sin(phase);
            }
            return (re * re + im * im) / (end - offset);
        }

        private float MeasureLoudness(float[] samples)
        {
            float sumSq = 0f;
            foreach (float s in samples) sumSq += s * s;
            float rms = MathF.Sqrt(sumSq / samples.Length);
            return rms < 1e-9f ? -96f : 20f * MathF.Log10(rms);
        }
    }
}
