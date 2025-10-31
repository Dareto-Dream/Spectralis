using System;
using System.Collections.Generic;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class ParticleSystemVisualizer : SkiaVisualizerBase
    {
        public override string Id => "particles";
        public override string DisplayName => "Particle System";
        public override string Category => "Particles";

        private readonly struct Particle
        {
            public float X { get; init; }
            public float Y { get; init; }
            public float Vx { get; init; }
            public float Vy { get; init; }
            public float Life { get; init; }
            public float Size { get; init; }
            public SKColor Color { get; init; }

            public Particle Tick() => this with
            {
                X = X + Vx,
                Y = Y + Vy,
                Vy = Vy + 0.04f,
                Life = Life - 0.015f
            };
        }

        private readonly List<Particle> _particles = new(512);
        private readonly Random _rng = new();

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(10, 10, 18));
            if (Spectrum.Length == 0) return;

            float w = (float)width;
            float h = (float)height;

            int bandsToSpawn = Math.Min(8, Spectrum.Length);

            for (int i = 0; i < bandsToSpawn; i++)
            {
                float bandEnergy = Spectrum[i * Spectrum.Length / bandsToSpawn];

                if (bandEnergy > 0.1f && _particles.Count < 400)
                {
                    float hue = (i / (float)bandsToSpawn) * 300f;
                    float spawnX = w * (i + 0.5f) / bandsToSpawn;
                    float speed = bandEnergy * 4f + 0.5f;

                    _particles.Add(new Particle
                    {
                        X = spawnX + (float)(_rng.NextDouble() * 20 - 10),
                        Y = h,
                        Vx = (float)(_rng.NextDouble() * 2 - 1),
                        Vy = -(speed + (float)_rng.NextDouble()),
                        Life = 1f,
                        Size = bandEnergy * 6f + 1.5f,
                        Color = HsvToColor(hue, 0.9f, 0.95f)
                    });
                }
            }

            using var paint = new SKPaint { IsAntialias = true };

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i].Tick();
                if (p.Life <= 0 || p.X < 0 || p.X > w || p.Y < -20)
                {
                    _particles.RemoveAt(i);
                    continue;
                }
                _particles[i] = p;
                paint.Color = p.Color.WithAlpha((byte)(p.Life * 255));
                canvas.DrawCircle(p.X, p.Y, p.Size * p.Life, paint);
            }
        }
    }
}
