using System;
using System.Timers;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public class AudioPipeline : IAudioPipeline
    {
        private readonly FftProcessor _fft;
        private readonly WaveformBuffer _waveform;
        private readonly SpectrumSmoother _smoother;
        private readonly AudioLevelMonitor _levels;
        private readonly Timer _frameTimer;
        private bool _disposed;

        public event EventHandler<AudioFrame>? FrameReady;
        public bool IsActive { get; private set; }
        public int BandCount { get; }

        public AudioSampleTap? Tap { get; private set; }

        public AudioPipeline(int fftSize = 2048, int bandCount = 64, int frameRateHz = 60)
        {
            BandCount = bandCount;
            _fft = new FftProcessor(fftSize, bandCount);
            _waveform = new WaveformBuffer(fftSize);
            _smoother = new SpectrumSmoother(bandCount);
            _levels = new AudioLevelMonitor();

            double intervalMs = 1000.0 / frameRateHz;
            _frameTimer = new Timer(intervalMs);
            _frameTimer.Elapsed += OnFrameTick;
            _frameTimer.AutoReset = true;
        }

        public AudioSampleTap CreateTap(ISampleProvider source)
        {
            Tap = new AudioSampleTap(source, _waveform, _fft);
            return Tap;
        }

        public void Start()
        {
            IsActive = true;
            _frameTimer.Start();
        }

        public void Stop()
        {
            IsActive = false;
            _frameTimer.Stop();
            _fft.ComputeBands();
            _smoother.Reset();
            _waveform.Clear();
        }

        private void OnFrameTick(object? sender, ElapsedEventArgs e)
        {
            try
            {
                float[] rawBands = _fft.ComputeBands();
                float[] smoothed = _smoother.Smooth(rawBands);
                float[] waveSnap = _waveform.CopySnapshot();

                var frame = new AudioFrame(
                    smoothed,
                    waveSnap,
                    _levels.RmsLeft,
                    _levels.RmsRight,
                    _levels.PeakLeft,
                    _levels.PeakRight,
                    DateTime.UtcNow.TimeOfDay
                );

                FrameReady?.Invoke(this, frame);
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _frameTimer.Stop();
            _frameTimer.Dispose();
        }
    }
}
