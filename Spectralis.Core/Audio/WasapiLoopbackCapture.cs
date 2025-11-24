using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public class WasapiLoopbackCaptureAdapter : IAudioCapture
    {
        private NAudio.CoreAudioApi.WasapiLoopbackCapture? _capture;
        private MMDeviceEnumerator? _enumerator;
        private MMDevice? _device;
        private bool _disposed;
        private bool _isCapturing;

        public event EventHandler<WaveInEventArgs>? DataAvailable;
        public event EventHandler<StoppedEventArgs>? RecordingStopped;

        public WaveFormat WaveFormat => _capture?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
        public bool IsCapturing => _isCapturing;

        public WasapiLoopbackCaptureAdapter()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new PlatformNotSupportedException("WASAPI loopback is Windows-only");
        }

        public void StartCapture()
        {
            DisposeCapture();

            _enumerator = new MMDeviceEnumerator();
            try
            {
                _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _capture = new NAudio.CoreAudioApi.WasapiLoopbackCapture(_device);
            }
            catch
            {
                _enumerator.Dispose();
                _enumerator = null;
                _device?.Dispose();
                _device = null;
                throw;
            }

            _capture.DataAvailable += (s, e) => DataAvailable?.Invoke(this, e);
            _capture.RecordingStopped += (s, e) =>
            {
                _isCapturing = false;
                RecordingStopped?.Invoke(this, e);
            };
            _capture.StartRecording();
            _isCapturing = true;
        }

        public void StopCapture()
        {
            _capture?.StopRecording();
            _isCapturing = false;
            DisposeCapture();
        }

        private void DisposeCapture()
        {
            _capture?.Dispose();
            _capture = null;
            _device?.Dispose();
            _device = null;
            _enumerator?.Dispose();
            _enumerator = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            DisposeCapture();
        }
    }
}
