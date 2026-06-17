using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class VuMeterVisualizer : SkiaVisualizerBase
    {
        public override string Id => "vu-meter";
        public override string DisplayName => "VU Meter";
        public override string Category => "Level";

        private float _smoothL, _smoothR, _peakL, _peakR;
        private int _peakHoldL, _peakHoldR;
        private const int PeakHoldFrames = 45;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(14, 14, 20));

            _smoothL = _smoothL * 0.8f + RmsLeft * 0.2f;
            _smoothR = _smoothR * 0.8f + RmsRight * 0.2f;

            if (RmsLeft > _peakL) { _peakL = RmsLeft; _peakHoldL = PeakHoldFrames; }
            else if (--_peakHoldL <= 0) _peakL = Math.Max(0, _peakL - 0.005f);
            if (RmsRight > _peakR) { _peakR = RmsRight; _peakHoldR = PeakHoldFrames; }
            else if (--_peakHoldR <= 0) _peakR = Math.Max(0, _peakR - 0.005f);

            float w = (float)width;
            float h = (float)height;
            float channelW = w / 2f - 8f;
            float margin = 4f;

            DrawChannel(canvas, margin, 0, channelW, h, _smoothL, _peakL, "L");
            DrawChannel(canvas, w / 2f + margin, 0, channelW, h, _smoothR, _peakR, "R");
        }

        private static void DrawChannel(SKCanvas canvas, float x, float y, float w, float h, float level, float peak, string label)
        {
            float barH = h - 20f;

            using var bgPaint = new SKPaint { Color = new SKColor(20, 20, 30) };
            canvas.DrawRect(x, y + 10, w, barH, bgPaint);

            int segments = 20;
            float segH = barH / segments;
            int filledSegs = (int)(level * segments);

            for (int i = 0; i < segments; i++)
            {
                float sy = y + 10 + barH - (i + 1) * segH;
                float fraction = (float)i / segments;
                var color = fraction < 0.6f ? new SKColor(0, 180, 80) :
                            fraction < 0.85f ? new SKColor(200, 180, 0) : new SKColor(220, 40, 40);

                if (i < filledSegs)
                {
                    using var p = new SKPaint { Color = color };
                    canvas.DrawRect(x + 1, sy + 1, w - 2, segH - 2, p);
                }
            }

            float peakY = y + 10 + barH - peak * barH;
            using var peakPaint = new SKPaint { Color = SKColors.White, StrokeWidth = 2f, Style = SKPaintStyle.Stroke };
            canvas.DrawLine(x, peakY, x + w, peakY, peakPaint);

            using var labelPaint = new SKPaint
            {
                Color = new SKColor(150, 150, 180),
                TextSize = 11f,
                IsAntialias = true
            };
            canvas.DrawText(label, x + w / 2f - 4f, y + h - 2f, labelPaint);
        }
    }
}
