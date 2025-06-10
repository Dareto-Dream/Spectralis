using System;
using System.Drawing;

namespace Spectralis.Visualizers
{
    internal struct Star
    {
        public float X, Y, Z, PrevZ;
    }

    public class StarfieldVisualizer : VisualizerBase
    {
        public override string Name => "Starfield";

        private readonly Star[] _stars;
        private readonly Random _rng = new Random();
        private float _speed = 2f;

        public StarfieldVisualizer(int starCount = 300)
        {
            _stars = new Star[starCount];
            for (int i = 0; i < starCount; i++)
                ResetStar(ref _stars[i], true);
        }

        private void ResetStar(ref Star s, bool randomDepth = false)
        {
            s.X = (float)(_rng.NextDouble() * 2 - 1);
            s.Y = (float)(_rng.NextDouble() * 2 - 1);
            s.Z = randomDepth ? (float)_rng.NextDouble() : 1f;
            s.PrevZ = s.Z;
        }

        public override void Render(Graphics g, Rectangle bounds, float[] spectrum, float[] waveform)
        {
            g.FillRectangle(Brushes.Black, bounds);

            float energy = 0;
            if (spectrum != null && spectrum.Length > 0)
            {
                for (int i = 0; i < Math.Min(8, spectrum.Length); i++)
                    energy += spectrum[i];
                energy /= 8f;
            }

            _speed = 1f + energy * 8f;

            float cx = bounds.Left + bounds.Width / 2f;
            float cy = bounds.Top + bounds.Height / 2f;
            float scale = Math.Min(bounds.Width, bounds.Height) / 2f;

            for (int i = 0; i < _stars.Length; i++)
            {
                ref Star s = ref _stars[i];
                s.PrevZ = s.Z;
                s.Z -= _speed * 0.005f;

                if (s.Z <= 0)
                {
                    ResetStar(ref s);
                    continue;
                }

                float sx = (s.X / s.Z) * scale + cx;
                float sy = (s.Y / s.Z) * scale + cy;
                float px = (s.X / s.PrevZ) * scale + cx;
                float py = (s.Y / s.PrevZ) * scale + cy;

                if (sx < bounds.Left || sx > bounds.Right || sy < bounds.Top || sy > bounds.Bottom)
                {
                    ResetStar(ref s);
                    continue;
                }

                float brightness = 1f - s.Z;
                int alpha = (int)(brightness * 220);
                float size = Math.Max(0.5f, brightness * 3f);

                using var pen = new Pen(Color.FromArgb(alpha, 200, 220, 255), size);
                g.DrawLine(pen, px, py, sx, sy);
            }
        }

        public override void Reset()
        {
            for (int i = 0; i < _stars.Length; i++)
                ResetStar(ref _stars[i], true);
        }
    }
}
