using NAudio.Dsp;
using NAudio.Wave;

namespace Spectralis.Core.Analysis;

/// <summary>Krumhansl-Schmuckler key estimation over a 30-second chromagram.</summary>
public static class KeyAnalyzer
{
    private static readonly double[] MajorProfile =
        [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];

    private static readonly double[] MinorProfile =
        [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    private static readonly string[] NoteNames =
        ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    private const int FftSize = 4096;
    private const int HopSamples = 2048;
    private const int MaxSeconds = 30;

    /// <summary>Reads the first 30 seconds of a file as mono and estimates its key.</summary>
    public static string AnalyzeFile(string path)
    {
        try
        {
            using var reader = new AudioFileReader(path);
            var sr = reader.WaveFormat.SampleRate;
            var ch = reader.WaveFormat.Channels;
            var maxRead = Math.Min(MaxSeconds * sr * ch, (int)(reader.Length / sizeof(float)));
            var raw = new float[maxRead];
            var totalRead = reader.Read(raw, 0, maxRead);

            var monoLen = totalRead / ch;
            var mono = new float[monoLen];
            for (var i = 0; i < monoLen; i++)
            {
                var sum = 0f;
                for (var c = 0; c < ch; c++)
                {
                    sum += raw[(i * ch) + c];
                }

                mono[i] = sum / ch;
            }

            return Analyze(mono, sr);
        }
        catch
        {
            return "";
        }
    }

    public static string Analyze(float[] monoSamples, int sampleRate)
    {
        if (monoSamples.Length < FftSize)
        {
            return "";
        }

        var chroma = new double[12];
        var samples = Math.Min(monoSamples.Length, MaxSeconds * sampleRate);
        var fftBuf = new Complex[FftSize];
        var window = BuildHannWindow(FftSize);

        var frameCount = (samples - FftSize) / HopSamples;
        for (var f = 0; f < frameCount; f++)
        {
            var offset = f * HopSamples;
            for (var i = 0; i < FftSize; i++)
            {
                fftBuf[i].X = monoSamples[offset + i] * window[i];
                fftBuf[i].Y = 0f;
            }

            FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), fftBuf);

            for (var bin = 1; bin < FftSize / 2; bin++)
            {
                var freq = bin * sampleRate / (double)FftSize;
                if (freq < 32 || freq > 4200)
                {
                    continue;
                }

                var midiNote = 69 + (12 * Math.Log2(freq / 440.0));
                var pitchClass = (((int)Math.Round(midiNote) % 12) + 12) % 12;

                var mag = Math.Sqrt((fftBuf[bin].X * fftBuf[bin].X) + (fftBuf[bin].Y * fftBuf[bin].Y));
                chroma[pitchClass] += mag;
            }
        }

        if (chroma.All(v => v == 0))
        {
            return "";
        }

        var chromaMax = chroma.Max();
        for (var i = 0; i < 12; i++)
        {
            chroma[i] /= chromaMax;
        }

        // Best key by Pearson correlation against rotated K-S profiles.
        var bestCorr = double.MinValue;
        var bestKey = 0;
        var bestMajor = true;

        for (var root = 0; root < 12; root++)
        {
            var majorCorr = Correlation(chroma, RotateProfile(MajorProfile, root));
            var minorCorr = Correlation(chroma, RotateProfile(MinorProfile, root));

            if (majorCorr > bestCorr)
            {
                bestCorr = majorCorr;
                bestKey = root;
                bestMajor = true;
            }

            if (minorCorr > bestCorr)
            {
                bestCorr = minorCorr;
                bestKey = root;
                bestMajor = false;
            }
        }

        return $"{NoteNames[bestKey]} {(bestMajor ? "Major" : "Minor")}";
    }

    private static double[] RotateProfile(double[] profile, int steps)
    {
        var rotated = new double[12];
        for (var i = 0; i < 12; i++)
        {
            rotated[i] = profile[(i - steps + 12) % 12];
        }

        return rotated;
    }

    private static double Correlation(double[] a, double[] b)
    {
        var meanA = a.Average();
        var meanB = b.Average();
        var num = 0.0;
        var denA = 0.0;
        var denB = 0.0;
        for (var i = 0; i < 12; i++)
        {
            var da = a[i] - meanA;
            var db = b[i] - meanB;
            num += da * db;
            denA += da * da;
            denB += db * db;
        }

        var den = Math.Sqrt(denA * denB);
        return den < 1e-10 ? 0 : num / den;
    }

    private static float[] BuildHannWindow(int size)
    {
        var w = new float[size];
        for (var i = 0; i < size; i++)
        {
            w[i] = (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (size - 1))));
        }

        return w;
    }
}
