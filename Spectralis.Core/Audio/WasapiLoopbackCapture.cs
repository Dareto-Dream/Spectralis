using System;
using System.Runtime.InteropServices;
using NAudio.Wave;

namespace Spectralis.Core.Audio
{
    public class WasapiLoopbackCaptureAdapter : IAudioCapture
    {
        private NAudio.CoreAudioApi.WasapiLoopbackCapture? _capture;
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
            _capture = new NAudio.CoreAudioApi.WasapiLoopbackCapture();
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
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _capture?.Dispose();
        }
    }
}
