using System;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public class LoopbackCapture : IDisposable
    {
        private WasapiLoopbackCapture _capture;
        private readonly FftProcessor _fft;
        private readonly WaveformBuffer _waveform;
        private bool _running;
        private bool _disposed;

        public event EventHandler FrameReady;
        public float[] Spectrum => _fft.Bands;
        public float[] Waveform { get { _waveform.CopySnapshot(); return _waveform.Snapshot; } }

        public LoopbackCapture(int fftSize = 2048, int bandCount = 64)
        {
            _fft = new FftProcessor(fftSize, bandCount);
            _waveform = new WaveformBuffer(fftSize);
        }

        public void Start()
        {
            if (_running) return;

            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            _capture = new WasapiLoopbackCapture(device);
            _capture.DataAvailable += OnDataAvailable;
            _capture.StartRecording();
            _running = true;
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
            int sampleCount = e.BytesRecorded / bytesPerSample;
            var samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                samples[i] = BitConverter.ToSingle(e.Buffer, i * bytesPerSample);
            }

            _fft.Push(samples, 0, sampleCount);
            _waveform.Push(samples, 0, sampleCount);
            FrameReady?.Invoke(this, EventArgs.Empty);
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            _capture?.StopRecording();
            _capture?.Dispose();
            _capture = null;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;
            if (disposing)
                Stop();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
