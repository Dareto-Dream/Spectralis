using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class AudioReactiveGradientVisualizer : SkiaVisualizerBase
    {
        public override string Id => "reactive-gradient";
        public override string DisplayName => "Reactive Gradient";
        public override string Category => "Ambient";

        private float _t;
        private float _smoothBass, _smoothMid, _smoothHigh;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            _t += 0.008f;

            float bass = 0f, mid = 0f, high = 0f;
            int len = Spectrum.Length;
            for (int i = 0; i < len / 3; i++) bass += Spectrum[i];
            for (int i = len / 3; i < 2 * len / 3; i++) mid += Spectrum[i];
            for (int i = 2 * len / 3; i < len; i++) high += Spectrum[i];
            bass /= Math.Max(1, len / 3);
            mid /= Math.Max(1, len / 3);
            high /= Math.Max(1, len / 3);

            _smoothBass = _smoothBass * 0.85f + bass * 0.15f;
            _smoothMid = _smoothMid * 0.85f + mid * 0.15f;
            _smoothHigh = _smoothHigh * 0.85f + high * 0.15f;

            float hue1 = (_t * 40f + _smoothBass * 60f) % 360f;
            float hue2 = (hue1 + 60f + _smoothMid * 40f) % 360f;
            float hue3 = (hue1 + 180f + _smoothHigh * 30f) % 360f;

            float bv = 0.2f + _smoothBass * 0.8f;
            float mv = 0.2f + _smoothMid * 0.8f;
            float hv = 0.2f + _smoothHigh * 0.8f;

            float cx = (float)(width / 2);
            float cy = (float)(height / 2);

            using var bgShader = SKShader.CreateRadialGradient(
                new SKPoint(cx, cy), (float)Math.Max(width, height),
                new[] { HsvToColor(hue1, 0.9f, bv), HsvToColor(hue3, 0.7f, 0.05f) },
                SKShaderTileMode.Clamp);

            using var fgShader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0), new SKPoint((float)width, (float)height),
                new[] { HsvToColor(hue2, 0.8f, mv, 180), HsvToColor(hue3, 0.9f, hv, 100) },
                SKShaderTileMode.Clamp);

            using var bgPaint = new SKPaint { Shader = bgShader };
            using var fgPaint = new SKPaint { Shader = fgShader };

            canvas.DrawRect(0, 0, (float)width, (float)height, bgPaint);
            canvas.DrawRect(0, 0, (float)width, (float)height, fgPaint);
        }
    }
}
