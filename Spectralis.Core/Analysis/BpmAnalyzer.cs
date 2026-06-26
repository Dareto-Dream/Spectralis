using NAudio.Wave;

namespace Spectralis.Core.Analysis;

/// <summary>Energy-envelope autocorrelation BPM detector over the first minute.</summary>
public static class BpmAnalyzer
{
    private const int HopSamples = 512;
    private const int AnalyzeSeconds = 60;
    private const int MinBpm = 60;
    private const int MaxBpm = 200;

    public static async Task<(float Bpm, TimeSpan FirstBeatOffset)> AnalyzeAsync(
        string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var reader = new AudioFileReader(filePath);
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels = reader.WaveFormat.Channels;
            var maxSamples = AnalyzeSeconds * sampleRate * channels;

            var rawBuffer = new float[maxSamples + HopSamples];
            var totalRead = 0;
            var tmp = new float[4096];
            while (totalRead < maxSamples)
            {
                ct.ThrowIfCancellationRequested();
                var read = reader.Read(tmp, 0, Math.Min(tmp.Length, maxSamples - totalRead));
                if (read == 0)
                {
                    break;
                }

                Array.Copy(tmp, 0, rawBuffer, totalRead, read);
                totalRead += read;
            }

            var monoLength = totalRead / channels;
            var mono = new float[monoLength];
            for (var i = 0; i < monoLength; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += rawBuffer[(i * channels) + c];
                }

                mono[i] = sum / channels;
            }

            ct.ThrowIfCancellationRequested();

            // Energy envelope per hop frame.
            var frameCount = monoLength / HopSamples;
            if (frameCount < 8)
            {
                return (120f, TimeSpan.Zero);
            }

            var energy = new float[frameCount];
            for (var f = 0; f < frameCount; f++)
            {
                var offset = f * HopSamples;
                var sum = 0.0;
                var count = Math.Min(HopSamples, monoLength - offset);
                for (var i = 0; i < count; i++)
                {
                    sum += mono[offset + i] * mono[offset + i];
                }

                energy[f] = (float)Math.Sqrt(sum / count);
            }

            // Onset function: positive first-difference of energy.
            var onset = new float[frameCount];
            for (var f = 1; f < frameCount; f++)
            {
                onset[f] = Math.Max(0f, energy[f] - energy[f - 1]);
            }

            ct.ThrowIfCancellationRequested();

            var fps = (double)sampleRate / HopSamples;
            var lagMin = Math.Max(1, (int)Math.Round(fps * 60.0 / MaxBpm));
            var lagMax = Math.Min(frameCount - 1, (int)Math.Round(fps * 60.0 / MinBpm));

            // Autocorrelation of the onset function over the BPM lag range.
            var bestLag = lagMin;
            var bestCorr = double.MinValue;
            for (var lag = lagMin; lag <= lagMax; lag++)
            {
                ct.ThrowIfCancellationRequested();
                var corr = 0.0;
                var count = frameCount - lag;
                for (var i = 0; i < count; i++)
                {
                    corr += onset[i] * onset[i + lag];
                }

                corr /= count;
                if (corr > bestCorr)
                {
                    bestCorr = corr;
                    bestLag = lag;
                }
            }

            var bpm = (float)(fps * 60.0 / bestLag);
            while (bpm < MinBpm)
            {
                bpm *= 2;
            }

            while (bpm > MaxBpm)
            {
                bpm /= 2;
            }

            bpm = (float)Math.Round(bpm, 1);

            return (bpm, FindFirstBeat(onset, bestLag, fps));
        }, ct);
    }

    private static TimeSpan FindFirstBeat(float[] onset, int period, double fps)
    {
        if (onset.Length == 0)
        {
            return TimeSpan.Zero;
        }

        // Strongest onset peak within the first two beat periods.
        var searchEnd = Math.Min(onset.Length, period * 2);
        var bestFrame = 0;
        var bestVal = 0f;
        for (var i = 0; i < searchEnd; i++)
        {
            if (onset[i] > bestVal)
            {
                bestVal = onset[i];
                bestFrame = i;
            }
        }

        return TimeSpan.FromSeconds(bestFrame / fps);
    }
}
