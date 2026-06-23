using NAudio.Wave;

namespace Spectralis.Tests.Core;

/// <summary>Generates small WAV files on disk for engine tests.</summary>
public static class WavFixture
{
    /// <summary>Writes a 440 Hz sine WAV and returns its path. Caller owns deletion.</summary>
    public static string CreateSineWav(double seconds = 0.5, int sampleRate = 44100, int channels = 2)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spectralis-test-{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(sampleRate, 16, channels);
        using var writer = new WaveFileWriter(path, format);

        var totalFrames = (int)(seconds * sampleRate);
        var samples = new float[channels];
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var value = (float)(0.25 * Math.Sin(2 * Math.PI * 440 * frame / sampleRate));
            for (var ch = 0; ch < channels; ch++)
            {
                samples[ch] = value;
            }

            writer.WriteSamples(samples, 0, channels);
        }

        return path;
    }

    /// <summary>Writes a click-track WAV (short bursts at the given tempo) for BPM tests.</summary>
    public static string CreateClickTrackWav(double bpm = 120, double seconds = 30, int sampleRate = 44100)
    {
        var path = Path.Combine(Path.GetTempPath(), $"spectralis-click-{Guid.NewGuid():N}.wav");
        var format = new WaveFormat(sampleRate, 16, 1);
        using var writer = new WaveFileWriter(path, format);

        var beatPeriodFrames = (int)(60.0 / bpm * sampleRate);
        var clickFrames = sampleRate / 50;  // 20ms burst
        var totalFrames = (int)(seconds * sampleRate);
        var sample = new float[1];
        for (var frame = 0; frame < totalFrames; frame++)
        {
            var phase = frame % beatPeriodFrames;
            sample[0] = phase < clickFrames
                ? (float)(0.8 * Math.Sin(2 * Math.PI * 1000 * phase / sampleRate) * Math.Exp(-phase / (sampleRate / 200.0)))
                : 0f;
            writer.WriteSamples(sample, 0, 1);
        }

        return path;
    }
}
