using System;
using System.Collections.Generic;
using System.Drawing;

namespace Spectralis.Visualizers
{
    internal class Particle
    {
        public float X, Y, VX, VY, Life, MaxLife, Size;
        public Color Color;
    }

    public class ParticleVisualizer : VisualizerBase
    {
        public override string Name => "Particles";

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly Random _rng = new Random();
        private const int MaxParticles = 400;

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.FillRectangle(new SolidBrush(Color.FromArgb(15, 0, 0, 0)), bounds);

            float energy = 0;
            if (spectrum != null && spectrum.Length > 0)
            {
                for (int i = 0; i < Math.Min(16, spectrum.Length); i++)
                    energy += spectrum[i];
                energy /= 16f;
            }

            int spawnCount = (int)(energy * 20);
            for (int i = 0; i < spawnCount && _particles.Count < MaxParticles; i++)
            {
                float cx = bounds.Left + bounds.Width / 2f;
                float cy = bounds.Top + bounds.Height / 2f;
                float angle = (float)(_rng.NextDouble() * Math.PI * 2);
                float speed = (float)(energy * 3 + _rng.NextDouble() * 2);

                int r = _rng.Next(80, 255);
                int b = _rng.Next(100, 255);
                _particles.Add(new Particle
                {
                    X = cx,
                    Y = cy,
                    VX = (float)Math.Cos(angle) * speed,
                    VY = (float)Math.Sin(angle) * speed,
                    Life = 1f,
                    MaxLife = 1f,
                    Size = (float)(1.5 + _rng.NextDouble() * 3 * energy),
                    Color = Color.FromArgb(r, 120, b)
                });
            }

            for (int i = _particles.Count - 1; i >= 0; i--)
            {
                var p = _particles[i];
                p.X += p.VX;
                p.Y += p.VY;
                p.VY += 0.05f;
                p.Life -= 0.02f;

                if (p.Life <= 0 ||
                    p.X < bounds.Left || p.X > bounds.Right ||
                    p.Y < bounds.Top || p.Y > bounds.Bottom)
                {
                    _particles.RemoveAt(i);
                    continue;
                }

                int alpha = (int)(p.Life / p.MaxLife * 200);
                using var brush = new SolidBrush(Color.FromArgb(alpha, p.Color));
                g.FillEllipse(brush, p.X - p.Size / 2, p.Y - p.Size / 2, p.Size, p.Size);
            }
        }

        public override void Reset()
        {
            _particles.Clear();
        }
    }
}
