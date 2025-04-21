using System;
using System.Drawing;

namespace Spectralis.Visualizers
{
    public interface IVisualizer : IDisposable
    {
        string Name { get; }
        void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform);
        void Reset();
    }
}
