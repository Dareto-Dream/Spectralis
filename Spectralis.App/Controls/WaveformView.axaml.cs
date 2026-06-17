using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.Core.Audio;

namespace Spectralis.App.Controls
{
    public partial class WaveformView : UserControl
    {
        public static readonly StyledProperty<IAudioPipeline?> PipelineProperty =
            AvaloniaProperty.Register<WaveformView, IAudioPipeline?>(nameof(Pipeline));

        public static readonly StyledProperty<IBrush> WaveformBrushProperty =
            AvaloniaProperty.Register<WaveformView, IBrush>(nameof(WaveformBrush), Brushes.White);

        public IAudioPipeline? Pipeline
        {
            get => GetValue(PipelineProperty);
            set => SetValue(PipelineProperty, value);
        }

        public IBrush WaveformBrush
        {
            get => GetValue(WaveformBrushProperty);
            set => SetValue(WaveformBrushProperty, value);
        }

        private float[] _waveform = Array.Empty<float>();

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

        private void OnFrameReady(object? sender, AudioFrame frame)
        {
            _waveform = (float[])frame.Waveform.Clone();
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            if (_waveform.Length < 2) return;

            double w = Bounds.Width;
            double h = Bounds.Height;
            double half = h / 2.0;
            double step = w / (_waveform.Length - 1);

            var geo = new StreamGeometry();
            using (var sgc = geo.Open())
            {
                sgc.BeginFigure(new Point(0, half - _waveform[0] * half), false);
                for (int i = 1; i < _waveform.Length; i++)
                    sgc.LineTo(new Point(i * step, half - _waveform[i] * half));
            }

            ctx.DrawGeometry(null, new Pen(WaveformBrush, 1.5), geo);
        }
    }
}
