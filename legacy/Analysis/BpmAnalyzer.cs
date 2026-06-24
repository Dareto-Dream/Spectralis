using NAudio.Wave;

namespace Spectralis;

internal static class BpmAnalyzer
{
    private const int HopSamples   = 512;
    private const int AnalyzeSeconds = 60;
    private const int MinBpm       = 60;
    private const int MaxBpm       = 200;

    public static async Task<(float Bpm, TimeSpan FirstBeatOffset)> AnalyzeAsync(
        string filePath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            using var reader = new AudioFileReader(filePath);
            var sampleRate = reader.WaveFormat.SampleRate;
            var channels   = reader.WaveFormat.Channels;
            var maxSamples = AnalyzeSeconds * sampleRate * channels;

            // Read mono samples
            var rawBuffer = new float[maxSamples + HopSamples];
            var totalRead = 0;
            var tmp = new float[4096];
            while (totalRead < maxSamples)
            {
                ct.ThrowIfCancellationRequested();
                var read = reader.Read(tmp, 0, Math.Min(tmp.Length, maxSamples - totalRead));
                if (read == 0) break;
                Array.Copy(tmp, 0, rawBuffer, totalRead, read);
                totalRead += read;
            }

            // Mix to mono
            var monoLength = totalRead / channels;
            var mono = new float[monoLength];
            for (var i = 0; i < monoLength; i++)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                    sum += rawBuffer[i * channels + c];
                mono[i] = sum / channels;
            }

            ct.ThrowIfCancellationRequested();

            // Energy envelope
            var frameCount = monoLength / HopSamples;
            if (frameCount < 8) return (120f, TimeSpan.Zero);

            var energy = new float[frameCount];
            for (var f = 0; f < frameCount; f++)
            {
                var offset = f * HopSamples;
                var sum = 0.0;
                var count = Math.Min(HopSamples, monoLength - offset);
                for (var i = 0; i < count; i++)
                    sum += mono[offset + i] * mono[offset + i];
                energy[f] = (float)Math.Sqrt(sum / count);
            }

            // Onset function: positive first-difference of energy
            var onset = new float[frameCount];
            for (var f = 1; f < frameCount; f++)
                onset[f] = Math.Max(0f, energy[f] - energy[f - 1]);

            ct.ThrowIfCancellationRequested();

            // Frames per second
            var fps = (double)sampleRate / HopSamples;

            // Lag range for BPM [MinBpm, MaxBpm]
            var lagMin = (int)Math.Round(fps * 60.0 / MaxBpm);  // shorter period = higher BPM
            var lagMax = (int)Math.Round(fps * 60.0 / MinBpm);
            lagMin = Math.Max(1, lagMin);
            lagMax = Math.Min(frameCount - 1, lagMax);

            // Autocorrelation of onset function
            var bestLag  = lagMin;
            var bestCorr = double.MinValue;
            for (var lag = lagMin; lag <= lagMax; lag++)
            {
                ct.ThrowIfCancellationRequested();
                var corr = 0.0;
                var count = frameCount - lag;
                for (var i = 0; i < count; i++)
                    corr += onset[i] * onset[i + lag];
                corr /= count;
                if (corr > bestCorr) { bestCorr = corr; bestLag = lag; }
            }

            var bpm = (float)(fps * 60.0 / bestLag);

            // Clamp to sane range
            while (bpm < MinBpm) bpm *= 2;
            while (bpm > MaxBpm) bpm /= 2;
            bpm = (float)Math.Round(bpm, 1);

            // Find first beat offset: earliest strong onset peak at the beat period
            var firstBeatOffset = FindFirstBeat(onset, bestLag, fps);

            return (bpm, firstBeatOffset);
        }, ct);
    }

    private static TimeSpan FindFirstBeat(float[] onset, int period, double fps)
    {
        if (onset.Length == 0) return TimeSpan.Zero;

        // Find the onset peak in the first 2 periods
        var searchEnd = Math.Min(onset.Length, period * 2);
        var bestFrame = 0;
        var bestVal   = 0f;
        for (var i = 0; i < searchEnd; i++)
        {
            if (onset[i] > bestVal) { bestVal = onset[i]; bestFrame = i; }
        }

        return TimeSpan.FromSeconds(bestFrame / fps);
    }
}
