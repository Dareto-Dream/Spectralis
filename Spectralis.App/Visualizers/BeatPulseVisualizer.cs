using System;
using System.Collections.Generic;
using SkiaSharp;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Visualizers
{
    public class BeatPulseVisualizer : SkiaVisualizerBase
    {
        public override string Id => "beat-pulse";
        public override string DisplayName => "Beat Pulse";
        public override string Category => "Beat";

        private readonly BeatDetector _beat = new();
        private readonly List<(float R, float A, SKColor C)> _pulses = new();
        private float _phase;

        public override void OnFrameReady(in Spectralis.Core.Audio.AudioFrame frame)
        {
            base.OnFrameReady(frame);
            _beat.Process(frame);
            if (_beat.IsBeat)
            {
                float hue = (_phase * 120f) % 360f;
                _pulses.Add((0f, 1f, HsvToColor(hue, 0.85f, 1f)));
                _phase += 0.618f;
            }
        }

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(8, 8, 14));

            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float maxR = Math.Min(cx, cy);

            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

            for (int i = _pulses.Count - 1; i >= 0; i--)
            {
                var (r, a, color) = _pulses[i];
                float newR = r + maxR / 30f;
                float newA = a - 0.033f;
                if (newA <= 0 || newR > maxR) { _pulses.RemoveAt(i); continue; }
                _pulses[i] = (newR, newA, color);
                paint.Color = color.WithAlpha((byte)(newA * 255));
                paint.StrokeWidth = 3f * newA;
                canvas.DrawCircle(cx, cy, newR, paint);
            }

            float rms = (RmsLeft + RmsRight) / 2f;
            using var corePaint = new SKPaint
            {
                IsAntialias = true,
                Color = new SKColor(60, 40, 120, (byte)(rms * 200)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 20f)
            };
            canvas.DrawCircle(cx, cy, 40f + rms * 30f, corePaint);
        }
    }
}
