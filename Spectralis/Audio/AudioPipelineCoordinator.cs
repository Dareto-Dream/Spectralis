using System;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public class AudioPipelineCoordinator : IDisposable
    {
        private readonly FftProcessor _fft;
        private readonly WaveformBuffer _waveform;
        private readonly SpectrumSmoother _smoother;
        private readonly AudioLevelMonitor _levelMonitor;
        private AudioSampleTap _tap;
        private bool _disposed;

        public float[] Spectrum { get; private set; } = Array.Empty<float>();
        public float[] WaveformSnapshot { get; private set; } = Array.Empty<float>();
        public AudioLevelMonitor LevelMonitor => _levelMonitor;

        public event EventHandler FrameReady;

        public AudioPipelineCoordinator(int fftSize = 2048, int bandCount = 64)
        {
            _fft = new FftProcessor(fftSize, bandCount);
            _waveform = new WaveformBuffer(fftSize);
            _smoother = new SpectrumSmoother(bandCount);
            _levelMonitor = new AudioLevelMonitor();
        }

        public ISampleProvider CreateTap(ISampleProvider source)
        {
            _tap?.SamplesReady -= OnSamplesReady;
            _tap = new AudioSampleTap(source, _fft, _waveform);
            _tap.SamplesReady += OnSamplesReady;
            return _tap;
        }

        private void OnSamplesReady(object sender, EventArgs e)
        {
            var raw = _fft.Bands;
            Spectrum = _smoother.Smooth(raw);
            _waveform.CopySnapshot();
            WaveformSnapshot = _waveform.Snapshot;
            FrameReady?.Invoke(this, EventArgs.Empty);
        }

        public void Reset()
        {
            _fft.Reset();
            _waveform.Reset();
            _smoother.Reset();
            _levelMonitor.Reset();
            Spectrum = Array.Empty<float>();
            WaveformSnapshot = Array.Empty<float>();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_tap != null)
                _tap.SamplesReady -= OnSamplesReady;
        }
    }
}
