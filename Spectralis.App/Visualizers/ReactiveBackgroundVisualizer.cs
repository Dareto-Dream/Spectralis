using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class ReactiveBackgroundVisualizer : SkiaVisualizerBase
    {
        public override string Id => "reactive-bg";
        public override string DisplayName => "Reactive Background";
        public override string Category => "Ambient";

        private float _hue, _sat = 0.6f, _val = 0.3f;
        private float _smoothEnergy;

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            float energy = 0f;
            for (int i = 0; i < Math.Min(16, Spectrum.Length); i++) energy += Spectrum[i];
            energy /= Math.Max(1, Math.Min(16, Spectrum.Length));
            _smoothEnergy = _smoothEnergy * 0.9f + energy * 0.1f;

            _hue = (_hue + _smoothEnergy * 1.2f) % 360f;
            _val = 0.1f + _smoothEnergy * 0.6f;
            _sat = 0.5f + _smoothEnergy * 0.4f;

            var center = HsvToColor(_hue, _sat, _val);
            var edge = HsvToColor((_hue + 30f) % 360f, _sat * 0.8f, _val * 0.3f);

            using var shader = SKShader.CreateRadialGradient(
                new SKPoint((float)(width / 2), (float)(height / 2)),
                (float)Math.Max(width, height) * 0.7f,
                new[] { center, edge },
                SKShaderTileMode.Clamp);

            using var paint = new SKPaint { Shader = shader };
            canvas.DrawRect(0, 0, (float)width, (float)height, paint);
        }
    }
}
