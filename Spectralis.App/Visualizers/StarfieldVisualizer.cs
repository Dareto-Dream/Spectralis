using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class StarfieldVisualizer : SkiaVisualizerBase
    {
        public override string Id => "starfield";
        public override string DisplayName => "Starfield";
        public override string Category => "Particles";

        private const int StarCount = 200;
        private readonly float[] _sx = new float[StarCount];
        private readonly float[] _sy = new float[StarCount];
        private readonly float[] _sz = new float[StarCount];
        private readonly Random _rng = new();

        public StarfieldVisualizer()
        {
            for (int i = 0; i < StarCount; i++) Respawn(i, true);
        }

        private void Respawn(int i, bool randomZ = false)
        {
            _sx[i] = (float)(_rng.NextDouble() * 2 - 1);
            _sy[i] = (float)(_rng.NextDouble() * 2 - 1);
            _sz[i] = randomZ ? (float)_rng.NextDouble() : 1f;
        }

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(0, 0, 8));

            float cx = (float)(width / 2);
            float cy = (float)(height / 2);
            float scale = Math.Max(cx, cy);

            float energy = 0f;
            foreach (float s in Spectrum) energy += s;
            energy /= Math.Max(1, Spectrum.Length);

            float speed = 0.004f + energy * 0.02f;

            using var paint = new SKPaint { IsAntialias = true };

            for (int i = 0; i < StarCount; i++)
            {
                _sz[i] -= speed;
                if (_sz[i] <= 0) { Respawn(i); continue; }

                float xp = _sx[i] / _sz[i] * scale + cx;
                float yp = _sy[i] / _sz[i] * scale + cy;

                if (xp < 0 || xp > width || yp < 0 || yp > height)
                {
                    Respawn(i);
                    continue;
                }

                float size = (1 - _sz[i]) * 3f;
                byte brightness = (byte)((1 - _sz[i]) * 220 + 35);
                paint.Color = new SKColor(brightness, brightness, Math.Min((byte)255, (byte)(brightness + 30)));
                canvas.DrawCircle(xp, yp, Math.Max(0.5f, size), paint);
            }
        }
    }
}
