using System.Drawing;

namespace Spectralis.Visualizers
{
    public abstract class VisualizerBase : IVisualizer
    {
        private bool _disposed;

        public abstract string Name { get; }
        public abstract void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform);

        public virtual void Reset() { }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Dispose(true);
        }
    }
}
