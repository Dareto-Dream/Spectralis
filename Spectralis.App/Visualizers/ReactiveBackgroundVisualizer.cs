using System;
using SkiaSharp;
using Spectralis.Core.Visualizers;

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
            float bass = VisualizerContextHelper.BassEnergy(Spectrum);
            float mid = VisualizerContextHelper.MidEnergy(Spectrum);
            float energy = bass * 0.7f + mid * 0.3f;
            _smoothEnergy = VisualizerContextHelper.SmoothToward(_smoothEnergy, energy, 0.1f);

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
