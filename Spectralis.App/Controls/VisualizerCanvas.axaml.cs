using System;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using SkiaSharp;
using Spectralis.App.Visualizers;
using Spectralis.Core.Audio;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Controls
{
    public partial class VisualizerCanvas : UserControl, IDisposable
    {
        public static readonly StyledProperty<IAudioPipeline?> PipelineProperty =
            AvaloniaProperty.Register<VisualizerCanvas, IAudioPipeline?>(nameof(Pipeline));

        public IAudioPipeline? Pipeline
        {
            get => GetValue(PipelineProperty);
            set => SetValue(PipelineProperty, value);
        }

        private IVisualizer? _visualizer;
        private bool _disposed;

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (Pipeline != null) Pipeline.FrameReady += OnFrameReady;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (Pipeline != null) Pipeline.FrameReady -= OnFrameReady;
        }

        public void SetVisualizer(IVisualizer? viz)
        {
            _visualizer?.Dispose();
            _visualizer = viz;
            _visualizer?.OnSizeChanged(Bounds.Width, Bounds.Height);
        }

        private void OnFrameReady(object? sender, AudioFrame frame)
        {
            _visualizer?.OnFrameReady(frame);
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);
            _visualizer?.OnSizeChanged(e.NewSize.Width, e.NewSize.Height);
        }

        public override void Render(DrawingContext drawingContext)
        {
            base.Render(drawingContext);
            if (_visualizer == null) return;

            int w = (int)Math.Max(1, Bounds.Width);
            int h = (int)Math.Max(1, Bounds.Height);

            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            var ctx = new SkiaRenderContext(surface.Canvas, w, h);
            _visualizer.Render(ctx);

            using var snap = surface.Snapshot();
            using var data = snap.Encode(SKEncodedImageFormat.Png, 90);
            using var ms = new System.IO.MemoryStream(data.ToArray());
            var bitmap = new Avalonia.Media.Imaging.Bitmap(ms);
            drawingContext.DrawImage(bitmap, new Rect(0, 0, w, h));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _visualizer?.Dispose();
        }
    }
}
