using System;
using SkiaSharp;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Visualizers
{
    public class SkiaRenderContext : IVisualizerRenderContext
    {
        public SKCanvas Canvas { get; }
        public double Width { get; }
        public double Height { get; }

        public SkiaRenderContext(SKCanvas canvas, double width, double height)
        {
            Canvas = canvas;
            Width = width;
            Height = height;
        }

        public void Clear(float r, float g, float b, float a = 1f)
        {
            Canvas.Clear(new SKColor(
                (byte)(r * 255), (byte)(g * 255),
                (byte)(b * 255), (byte)(a * 255)));
        }
    }

    public abstract class SkiaVisualizerBase : IVisualizer
    {
        protected float[] Spectrum = Array.Empty<float>();
        protected float[] Waveform = Array.Empty<float>();
        protected float RmsLeft;
        protected float RmsRight;
        protected float PeakLeft;
        protected float PeakRight;
        protected double CanvasWidth;
        protected double CanvasHeight;
        private bool _disposed;

        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public virtual string Category => "Spectrum";
        public bool IsHardwareAccelerated => true;

        public void OnFrameReady(in AudioFrame frame)
        {
            Spectrum = (float[])frame.Spectrum.Clone();
            Waveform = (float[])frame.Waveform.Clone();
            RmsLeft = frame.RmsLeft;
            RmsRight = frame.RmsRight;
            PeakLeft = frame.PeakLeft;
            PeakRight = frame.PeakRight;
        }

        public void OnSizeChanged(double width, double height)
        {
            CanvasWidth = width;
            CanvasHeight = height;
        }

        public void Render(IVisualizerRenderContext ctx)
        {
            if (ctx is SkiaRenderContext skia)
                RenderSkia(skia.Canvas, skia.Width, skia.Height);
        }

        protected abstract void RenderSkia(SKCanvas canvas, double width, double height);

        protected static SKColor HsvToColor(float h, float s, float v, byte alpha = 255)
        {
            float r, g, b;
            if (s == 0) { r = g = b = v; }
            else
            {
                float sector = h / 60f;
                int i = (int)sector;
                float f = sector - i;
                float p = v * (1 - s);
                float q = v * (1 - s * f);
                float t = v * (1 - s * (1 - f));
                (r, g, b) = (i % 6) switch
                {
                    0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
                    3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q)
                };
            }
            return new SKColor((byte)(r * 255), (byte)(g * 255), (byte)(b * 255), alpha);
        }

        protected virtual void Dispose(bool disposing) { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
