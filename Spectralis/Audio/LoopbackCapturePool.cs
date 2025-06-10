using System;
using System.Collections.Generic;

namespace Spectralis.Audio
{
    public class LoopbackCapturePool : IDisposable
    {
        private readonly List<WeakReference<LoopbackCapture>> _instances = new List<WeakReference<LoopbackCapture>>();
        private bool _disposed;

        public LoopbackCapture Acquire(int fftSize = 2048, int bandCount = 64)
        {
            var capture = new LoopbackCapture(fftSize, bandCount);
            _instances.Add(new WeakReference<LoopbackCapture>(capture));
            return capture;
        }

        public void StopAll()
        {
            foreach (var wr in _instances)
            {
                if (wr.TryGetTarget(out var c))
                    c.Stop();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopAll();
        }
    }
}
