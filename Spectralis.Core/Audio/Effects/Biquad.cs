namespace Spectralis.Core.Audio.Effects;

/// <summary>
/// One second-order (biquad) section built from the RBJ "Audio EQ Cookbook"
/// formulas. Used both for real-time processing (<see cref="Process"/>, transposed
/// direct form II) and for drawing the EQ response curve (<see cref="MagnitudeDb(double,int)"/>).
/// NAudio's <c>BiQuadFilter</c> keeps its coefficients private, so the curve editor
/// could not reuse it for display — hence this local implementation.
/// </summary>
public sealed class Biquad
{
    private double _b0 = 1, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public void SetCoefficients(EqFilterType type, int sampleRate, double frequency, double q, double gainDb)
    {
        frequency = Math.Clamp(frequency, 10.0, (sampleRate / 2.0) - 1.0);
        q = Math.Max(0.05, q);

        var a = Math.Pow(10.0, gainDb / 40.0);
        var w0 = 2.0 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        var alpha = sin / (2.0 * q);

        double b0, b1, b2, a0, a1, a2;

        switch (type)
        {
            case EqFilterType.LowShelf:
            {
                var tsa = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) - ((a - 1) * cos) + tsa);
                b1 = 2 * a * ((a - 1) - ((a + 1) * cos));
                b2 = a * ((a + 1) - ((a - 1) * cos) - tsa);
                a0 = (a + 1) + ((a - 1) * cos) + tsa;
                a1 = -2 * ((a - 1) + ((a + 1) * cos));
                a2 = (a + 1) + ((a - 1) * cos) - tsa;
                break;
            }

            case EqFilterType.HighShelf:
            {
                var tsa = 2.0 * Math.Sqrt(a) * alpha;
                b0 = a * ((a + 1) + ((a - 1) * cos) + tsa);
                b1 = -2 * a * ((a - 1) + ((a + 1) * cos));
                b2 = a * ((a + 1) + ((a - 1) * cos) - tsa);
                a0 = (a + 1) - ((a - 1) * cos) + tsa;
                a1 = 2 * ((a - 1) - ((a + 1) * cos));
                a2 = (a + 1) - ((a - 1) * cos) - tsa;
                break;
            }

            case EqFilterType.LowPass:
            {
                b0 = (1 - cos) / 2;
                b1 = 1 - cos;
                b2 = (1 - cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            }

            case EqFilterType.HighPass:
            {
                b0 = (1 + cos) / 2;
                b1 = -(1 + cos);
                b2 = (1 + cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            }

            case EqFilterType.Notch:
            {
                b0 = 1;
                b1 = -2 * cos;
                b2 = 1;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            }

            default: // Peak
            {
                b0 = 1 + (alpha * a);
                b1 = -2 * cos;
                b2 = 1 - (alpha * a);
                a0 = 1 + (alpha / a);
                a1 = -2 * cos;
                a2 = 1 - (alpha / a);
                break;
            }
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public void Reset() => _z1 = _z2 = 0;

    public float Process(float x)
    {
        var y = (_b0 * x) + _z1;
        _z1 = (_b1 * x) - (_a1 * y) + _z2;
        _z2 = (_b2 * x) - (_a2 * y);
        return (float)y;
    }

    /// <summary>Magnitude response of this section at <paramref name="frequency"/> Hz, in dB.</summary>
    public double MagnitudeDb(double frequency, int sampleRate)
    {
        var w = 2.0 * Math.PI * frequency / sampleRate;
        double cos1 = Math.Cos(w), sin1 = Math.Sin(w);
        double cos2 = Math.Cos(2 * w), sin2 = Math.Sin(2 * w);

        var numRe = _b0 + (_b1 * cos1) + (_b2 * cos2);
        var numIm = -((_b1 * sin1) + (_b2 * sin2));
        var denRe = 1 + (_a1 * cos1) + (_a2 * cos2);
        var denIm = -((_a1 * sin1) + (_a2 * sin2));

        var numMag2 = (numRe * numRe) + (numIm * numIm);
        var denMag2 = (denRe * denRe) + (denIm * denIm);
        if (denMag2 < 1e-20)
        {
            return 0;
        }

        return 10.0 * Math.Log10(numMag2 / denMag2);
    }

    /// <summary>
    /// One-shot magnitude response (dB) for a band spec, without allocating persistent state.
    /// Used by the response-curve renderer.
    /// </summary>
    public static double MagnitudeDb(
        EqFilterType type,
        int sampleRate,
        double frequency,
        double q,
        double gainDb,
        double atFrequency)
    {
        var filter = new Biquad();
        filter.SetCoefficients(type, sampleRate, frequency, q, gainDb);
        return filter.MagnitudeDb(atFrequency, sampleRate);
    }
}
