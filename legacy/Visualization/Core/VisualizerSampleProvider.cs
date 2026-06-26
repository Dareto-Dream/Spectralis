using NAudio.Dsp;
using NAudio.Wave;

namespace Spectralis;

public sealed class VisualizerSampleProvider : ISampleProvider
{
    private const int FftLength = 4096;
    private const int FftBins = FftLength / 2;
    private const int SpectrumBarCount = 64;
    private const int WaveformPointCount = 256;
    private const int MinimumDecibels = -72;
    private const int SpectrogramHistoryLength = 512;
    private const int CorrelationBlockSize = 512;

    private readonly ISampleProvider source;
    private readonly int channels;
    private readonly object syncRoot = new();

    // Mono FFT (existing) + stereo FFT infrastructure
    private readonly Complex[] fftBuffer = new Complex[FftLength];
    private readonly Complex[] fftBufferL = new Complex[FftLength];
    private readonly Complex[] fftBufferR = new Complex[FftLength];
    private int fftPosition;

    private readonly float[] spectrumBars = new float[SpectrumBarCount];
    private readonly float[] rawFftBins = new float[FftBins];

    // Waveform rings (L and R; Waveform == WaveformL for mono compat)
    private readonly float[] waveformRingL = new float[WaveformPointCount];
    private readonly float[] waveformRingR = new float[WaveformPointCount];
    private int waveformPosition;

    // Legacy per-frame peak/RMS (kept for VisualizerScene consumers)
    private float peakLevel;
    private float rmsLevel;
    private double rmsAccumulator;
    private int rmsAccumulatorCount;

    // ITU-R BS.1770-4 K-weighting biquad (transposed direct form II, normalized a0=1)
    //
    // Stage 1 — high-shelf pre-filter
    //   Analog spec: f0=1681.974450955533 Hz, Q=0.7071752369554196, dBgain=+3.999843853973347 dB
    //   A  = 10^(dBgain/40)
    //   w0 = 2π·f0/Fs
    //   α  = sin(w0)/(2·Q)
    //   RBJ high-shelf (normalize each coeff by a0):
    //     b0 =  A·[(A+1)+(A-1)·cos(w0)+2·√A·α] / a0
    //     b1 = -2A·[(A-1)+(A+1)·cos(w0)]        / a0
    //     b2 =  A·[(A+1)+(A-1)·cos(w0)-2·√A·α] / a0
    //     a1 =  2·[(A-1)-(A+1)·cos(w0)]          / a0
    //     a2 =    [(A+1)-(A-1)·cos(w0)-2·√A·α]  / a0
    //   where a0 = (A+1)-(A-1)·cos(w0)+2·√A·α
    //
    // Stage 2 — high-pass filter
    //   Analog spec: f0=38.13547087602444 Hz, Q=0.5003270373238773
    //   w0 = 2π·f0/Fs,  α = sin(w0)/(2·Q)
    //   RBJ high-pass (normalize by a0 = 1+α):
    //     b0 =  (1+cos(w0))/2 / a0
    //     b1 = -(1+cos(w0))   / a0
    //     b2 =  (1+cos(w0))/2 / a0
    //     a1 = -2·cos(w0)     / a0
    //     a2 =  (1-α)         / a0
    private readonly double kS1b0, kS1b1, kS1b2, kS1a1, kS1a2;
    private readonly double kS2b0, kS2b1, kS2b2, kS2a1, kS2a2;

    // Biquad delay elements (transposed direct form II state)
    private double kS1z1, kS1z2;
    private double kS2z1, kS2z2;

    // LUFS sliding-window ring buffers (store per-sample K-weighted power)
    private readonly float[] lufsRingMom;   // 400 ms
    private readonly float[] lufsRingST;    // 3000 ms
    private int lufsPosMom;
    private int lufsPosST;
    private double lfsSumMom;
    private double lfsSumST;

    // RMS sliding-window ring buffers (unweighted mono power)
    private readonly float[] rmsRingFast;   // 300 ms
    private readonly float[] rmsRingSlow;   // 1000 ms
    private int rmsPosFast;
    private int rmsPosSlow;
    private double rmsSumFast;
    private double rmsSumSlow;

    // Stereo correlation accumulators (reset every CorrelationBlockSize samples)
    private double corrLR, corrLL, corrRR;
    private int corrCount;

    // Published metrics (written on FFT cycle, read by GetFrame)
    private float _lufsMomentary;
    private float _lufsShortTerm;
    private float _rmsFast;
    private float _rmsSlow;
    private float _stereoCorrelation;

    // Spectrogram ring buffer — allocated once, written each FFT cycle
    private readonly float[][] spectrogramBuffer;
    private int spectrogramNewestIndex;

    public VisualizerSampleProvider(ISampleProvider source)
    {
        this.source = source;
        channels = Math.Max(1, source.WaveFormat.Channels);

        var fs = source.WaveFormat.SampleRate;

        spectrogramBuffer = new float[SpectrogramHistoryLength][];
        for (var i = 0; i < SpectrogramHistoryLength; i++)
            spectrogramBuffer[i] = new float[FftBins];

        lufsRingMom = new float[(int)(0.400 * fs)];
        lufsRingST = new float[(int)(3.000 * fs)];
        rmsRingFast = new float[(int)(0.300 * fs)];
        rmsRingSlow = new float[(int)(1.000 * fs)];

        // --- Stage 1: high-shelf ---
        {
            const double f0 = 1681.974450955533;
            const double Q = 0.7071752369554196;
            const double dBgain = 3.999843853973347;
            var A = Math.Pow(10.0, dBgain / 40.0);
            var w0 = 2.0 * Math.PI * f0 / fs;
            var sinW0 = Math.Sin(w0);
            var cosW0 = Math.Cos(w0);
            var alpha = sinW0 / (2.0 * Q);
            var sqrtA = Math.Sqrt(A);
            var a0 = (A + 1) - (A - 1) * cosW0 + 2 * sqrtA * alpha;
            kS1b0 = A * ((A + 1) + (A - 1) * cosW0 + 2 * sqrtA * alpha) / a0;
            kS1b1 = -2 * A * ((A - 1) + (A + 1) * cosW0) / a0;
            kS1b2 = A * ((A + 1) + (A - 1) * cosW0 - 2 * sqrtA * alpha) / a0;
            kS1a1 = 2 * ((A - 1) - (A + 1) * cosW0) / a0;
            kS1a2 = ((A + 1) - (A - 1) * cosW0 - 2 * sqrtA * alpha) / a0;
        }

        // --- Stage 2: high-pass ---
        {
            const double f0 = 38.13547087602444;
            const double Q = 0.5003270373238773;
            var w0 = 2.0 * Math.PI * f0 / fs;
            var sinW0 = Math.Sin(w0);
            var cosW0 = Math.Cos(w0);
            var alpha = sinW0 / (2.0 * Q);
            var a0 = 1.0 + alpha;
            kS2b0 = (1 + cosW0) / 2.0 / a0;
            kS2b1 = -(1 + cosW0) / a0;
            kS2b2 = (1 + cosW0) / 2.0 / a0;
            kS2a1 = -2 * cosW0 / a0;
            kS2a2 = (1 - alpha) / a0;
        }
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var samplesRead = source.Read(buffer, offset, count);

        for (var index = 0; index < samplesRead; index += channels)
        {
            float left, right;
            if (channels >= 2)
            {
                left = buffer[offset + index];
                right = buffer[offset + index + 1];
            }
            else
            {
                left = right = buffer[offset + index];
            }

            CaptureStereoSample(left, right);
        }

        return samplesRead;
    }

    public VisualizerFrame GetFrame()
    {
        lock (syncRoot)
        {
            var waveformL = new float[WaveformPointCount];
            var waveformR = new float[WaveformPointCount];
            for (var i = 0; i < WaveformPointCount; i++)
            {
                var idx = (waveformPosition + i) % WaveformPointCount;
                waveformL[i] = waveformRingL[idx];
                waveformR[i] = waveformRingR[idx];
            }

            var spectrogramCopy = new float[SpectrogramHistoryLength][];
            for (var i = 0; i < SpectrogramHistoryLength; i++)
                spectrogramCopy[i] = (float[])spectrogramBuffer[i].Clone();

            return new VisualizerFrame(
                Spectrum: (float[])spectrumBars.Clone(),
                Waveform: waveformL,
                PeakLevel: peakLevel,
                RmsLevel: rmsLevel)
            {
                WaveformL = waveformL,
                WaveformR = waveformR,
                RawFftBins = (float[])rawFftBins.Clone(),
                LufsMomentary = _lufsMomentary,
                LufsShortTerm = _lufsShortTerm,
                RmsFast = _rmsFast,
                RmsSlow = _rmsSlow,
                StereoCorrelation = _stereoCorrelation,
                SpectrogramHistory = spectrogramCopy,
                SpectrogramNewestIndex = spectrogramNewestIndex,
            };
        }
    }

    public void FeedExternalSamples(float[] buffer, int offset, int count, int channels)
    {
        var ch = Math.Max(1, channels);
        for (var i = 0; i < count; i += ch)
        {
            float left, right;
            if (ch >= 2)
            {
                left = buffer[offset + i];
                right = buffer[offset + i + 1];
            }
            else
            {
                left = right = buffer[offset + i];
            }

            CaptureStereoSample(left, right);
        }
    }

    public void Clear()
    {
        lock (syncRoot)
        {
            Array.Clear(fftBuffer);
            Array.Clear(fftBufferL);
            Array.Clear(fftBufferR);
            Array.Clear(spectrumBars);
            Array.Clear(rawFftBins);
            Array.Clear(waveformRingL);
            Array.Clear(waveformRingR);
            fftPosition = 0;
            waveformPosition = 0;
            peakLevel = 0;
            rmsLevel = 0;
            rmsAccumulator = 0;
            rmsAccumulatorCount = 0;

            kS1z1 = kS1z2 = kS2z1 = kS2z2 = 0;

            Array.Clear(lufsRingMom);
            Array.Clear(lufsRingST);
            lufsPosMom = 0;
            lufsPosST = 0;
            lfsSumMom = 0;
            lfsSumST = 0;

            Array.Clear(rmsRingFast);
            Array.Clear(rmsRingSlow);
            rmsPosFast = 0;
            rmsPosSlow = 0;
            rmsSumFast = 0;
            rmsSumSlow = 0;

            corrLR = corrLL = corrRR = 0;
            corrCount = 0;

            _lufsMomentary = 0;
            _lufsShortTerm = 0;
            _rmsFast = 0;
            _rmsSlow = 0;
            _stereoCorrelation = 0;

            foreach (var row in spectrogramBuffer)
                Array.Clear(row);
            spectrogramNewestIndex = 0;
        }
    }

    private void CaptureStereoSample(float left, float right)
    {
        lock (syncRoot)
        {
            waveformRingL[waveformPosition] = left;
            waveformRingR[waveformPosition] = right;
            waveformPosition = (waveformPosition + 1) % WaveformPointCount;

            var mono = (left + right) * 0.5f;

            // Legacy peak + RMS (unchanged behaviour)
            peakLevel = Math.Max(Math.Abs(mono), peakLevel * 0.94f);
            rmsAccumulator += mono * mono;
            rmsAccumulatorCount++;
            if (rmsAccumulatorCount >= 256)
            {
                rmsLevel = (float)Math.Sqrt(rmsAccumulator / rmsAccumulatorCount);
                rmsAccumulator = 0;
                rmsAccumulatorCount = 0;
            }

            // Unweighted RMS sliding windows
            var monoSq = (float)(mono * mono);
            rmsSumFast += monoSq - rmsRingFast[rmsPosFast];
            rmsRingFast[rmsPosFast] = monoSq;
            rmsPosFast = (rmsPosFast + 1) % rmsRingFast.Length;

            rmsSumSlow += monoSq - rmsRingSlow[rmsPosSlow];
            rmsRingSlow[rmsPosSlow] = monoSq;
            rmsPosSlow = (rmsPosSlow + 1) % rmsRingSlow.Length;

            // K-weighting filter — transposed direct form II
            // Stage 1 (high-shelf): y1[n] = b0·x + z1;  z1 = b1·x - a1·y1 + z2;  z2 = b2·x - a2·y1
            var y1 = kS1b0 * mono + kS1z1;
            kS1z1 = kS1b1 * mono - kS1a1 * y1 + kS1z2;
            kS1z2 = kS1b2 * mono - kS1a2 * y1;
            // Stage 2 (high-pass)
            var y2 = kS2b0 * y1 + kS2z1;
            kS2z1 = kS2b1 * y1 - kS2a1 * y2 + kS2z2;
            kS2z2 = kS2b2 * y1 - kS2a2 * y2;

            // LUFS sliding windows
            var kSq = (float)(y2 * y2);
            lfsSumMom += kSq - lufsRingMom[lufsPosMom];
            lufsRingMom[lufsPosMom] = kSq;
            lufsPosMom = (lufsPosMom + 1) % lufsRingMom.Length;

            lfsSumST += kSq - lufsRingST[lufsPosST];
            lufsRingST[lufsPosST] = kSq;
            lufsPosST = (lufsPosST + 1) % lufsRingST.Length;

            // Stereo correlation — accumulate; publish every 512 samples
            corrLR += left * right;
            corrLL += left * left;
            corrRR += right * right;
            if (++corrCount >= CorrelationBlockSize)
            {
                var denom = Math.Sqrt(Math.Max(0, corrLL * corrRR));
                _stereoCorrelation = denom < 1e-12 ? 0f : (float)Math.Clamp(corrLR / denom, -1.0, 1.0);
                corrLR = corrLL = corrRR = 0;
                corrCount = 0;
            }

            // FFT accumulation
            var win = (float)FastFourierTransform.HammingWindow(fftPosition, FftLength);
            fftBuffer[fftPosition].X = mono * win;
            fftBuffer[fftPosition].Y = 0;
            fftBufferL[fftPosition].X = left * win;
            fftBufferL[fftPosition].Y = 0;
            fftBufferR[fftPosition].X = right * win;
            fftBufferR[fftPosition].Y = 0;
            fftPosition++;

            if (fftPosition < FftLength)
                return;

            var log2N = (int)Math.Log2(FftLength);
            FastFourierTransform.FFT(true, log2N, fftBuffer);
            FastFourierTransform.FFT(true, log2N, fftBufferL);
            FastFourierTransform.FFT(true, log2N, fftBufferR);

            UpdateSpectrumBars();
            UpdateRawFftBins();
            UpdateSpectrogramRow();
            UpdateMetrics();
            fftPosition = 0;
        }
    }

    private void UpdateSpectrumBars()
    {
        var nyquist = WaveFormat.SampleRate / 2.0;
        var minimumFrequency = 25.0;
        var maximumFrequency = Math.Min(18000.0, nyquist);
        var binCount = FftBins;

        for (var barIndex = 0; barIndex < spectrumBars.Length; barIndex++)
        {
            var startFrequency = GetLogFrequency(minimumFrequency, maximumFrequency, barIndex / (double)spectrumBars.Length);
            var endFrequency = GetLogFrequency(minimumFrequency, maximumFrequency, (barIndex + 1) / (double)spectrumBars.Length);

            var startBin = Math.Clamp((int)(startFrequency / nyquist * binCount), 1, binCount - 1);
            var endBin = Math.Clamp((int)(endFrequency / nyquist * binCount), startBin + 1, binCount);

            double energy = 0;
            for (var bin = startBin; bin < endBin; bin++)
            {
                var re = fftBuffer[bin].X;
                var im = fftBuffer[bin].Y;
                energy += Math.Sqrt((re * re) + (im * im));
            }

            var averageMagnitude = energy / (endBin - startBin);
            var decibels = 20 * Math.Log10(averageMagnitude + 1e-9);
            var normalized = (float)Math.Clamp((decibels - MinimumDecibels) / -MinimumDecibels, 0, 1);

            spectrumBars[barIndex] = Math.Max(normalized, spectrumBars[barIndex] * 0.84f);
        }
    }

    private void UpdateRawFftBins()
    {
        for (var bin = 0; bin < FftBins; bin++)
        {
            var re = fftBuffer[bin].X;
            var im = fftBuffer[bin].Y;
            var magnitude = Math.Sqrt((re * re) + (im * im));
            var db = 20.0 * Math.Log10(magnitude + 1e-9);
            rawFftBins[bin] = (float)Math.Clamp((db - MinimumDecibels) / -MinimumDecibels, 0, 1);
        }
    }

    private void UpdateSpectrogramRow()
    {
        spectrogramNewestIndex = (spectrogramNewestIndex + 1) % SpectrogramHistoryLength;
        Array.Copy(rawFftBins, spectrogramBuffer[spectrogramNewestIndex], FftBins);
    }

    private void UpdateMetrics()
    {
        // Absolute gate per ITU-R BS.1770-4: −70 LUFS → power = 10^((-70+0.691)/10)
        const double lufsGate = 1.1749e-7;

        var pmom = Math.Max(0, lfsSumMom) / lufsRingMom.Length;
        _lufsMomentary = pmom < lufsGate ? -70f : (float)(-0.691 + 10.0 * Math.Log10(pmom));

        var pst = Math.Max(0, lfsSumST) / lufsRingST.Length;
        _lufsShortTerm = pst < lufsGate ? -70f : (float)(-0.691 + 10.0 * Math.Log10(pst));

        _rmsFast = (float)Math.Sqrt(Math.Max(0, rmsSumFast) / rmsRingFast.Length);
        _rmsSlow = (float)Math.Sqrt(Math.Max(0, rmsSumSlow) / rmsRingSlow.Length);
    }

    private static double GetLogFrequency(double minimumFrequency, double maximumFrequency, double ratio)
    {
        var safeRatio = Math.Clamp(ratio, 0, 1);
        return minimumFrequency * Math.Pow(maximumFrequency / minimumFrequency, safeRatio);
    }
}
