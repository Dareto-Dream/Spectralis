using System;
using Spectralis.Core.Audio;

namespace Spectralis.Core.Visualizers
{
    public interface IVisualizer : IDisposable
    {
        string Id { get; }
        string DisplayName { get; }
        string Category { get; }
        bool IsHardwareAccelerated { get; }

        void OnFrameReady(in AudioFrame frame);
        void OnSizeChanged(double width, double height);
        void Render(IVisualizerRenderContext ctx);
    }

    public interface IVisualizerRenderContext
    {
        double Width { get; }
        double Height { get; }
        void Clear(float r, float g, float b, float a = 1f);
    }

    public class VisualizerInfo
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsHardwareAccelerated { get; set; }
        public Func<IVisualizer> Factory { get; set; } = null!;
    }
}
