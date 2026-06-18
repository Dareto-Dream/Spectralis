using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Spectralis.Core.Audio;

namespace Spectralis.App.Controls
{
    public partial class SpectrumBarsControl : UserControl
    {
        public static readonly StyledProperty<IAudioPipeline?> PipelineProperty =
            AvaloniaProperty.Register<SpectrumBarsControl, IAudioPipeline?>(nameof(Pipeline));

        public static readonly StyledProperty<IBrush> BarBrushProperty =
            AvaloniaProperty.Register<SpectrumBarsControl, IBrush>(nameof(BarBrush), Brushes.Cyan);

        public IAudioPipeline? Pipeline
        {
            get => GetValue(PipelineProperty);
            set => SetValue(PipelineProperty, value);
        }

        public IBrush BarBrush
        {
            get => GetValue(BarBrushProperty);
            set => SetValue(BarBrushProperty, value);
        }

        private float[] _spectrum = Array.Empty<float>();
        private Canvas? _canvas;

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _canvas = e.NameScope.Find<Canvas>("PART_Canvas");
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (Pipeline != null)
                Pipeline.FrameReady += OnFrameReady;
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            if (Pipeline != null)
                Pipeline.FrameReady -= OnFrameReady;
        }

        private void OnFrameReady(object? sender, Core.Audio.AudioFrame frame)
        {
            _spectrum = (float[])frame.Spectrum.Clone();
            Dispatcher.UIThread.Post(InvalidateVisual, DispatcherPriority.Render);
        }

        public override void Render(DrawingContext ctx)
        {
            base.Render(ctx);
            if (_spectrum.Length == 0) return;

            double w = Bounds.Width;
            double h = Bounds.Height;
            double barW = w / _spectrum.Length;

            for (int i = 0; i < _spectrum.Length; i++)
            {
                double barH = Math.Max(2, _spectrum[i] * h);
                double x = i * barW;
                ctx.FillRectangle(BarBrush, new Rect(x + 1, h - barH, Math.Max(1, barW - 2), barH));
            }
        }
    }
}
