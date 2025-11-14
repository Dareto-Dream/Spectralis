using System;
using SkiaSharp;

namespace Spectralis.Core.Visualizers
{
    public class ColorPalette
    {
        public static readonly ColorPalette Neon = new(
            new SKColor(255, 0, 128),
            new SKColor(0, 255, 200),
            new SKColor(120, 0, 255),
            new SKColor(255, 200, 0));

        public static readonly ColorPalette Fire = new(
            new SKColor(10, 0, 0),
            new SKColor(180, 20, 0),
            new SKColor(255, 140, 0),
            new SKColor(255, 240, 80));

        public static readonly ColorPalette Ocean = new(
            new SKColor(0, 10, 40),
            new SKColor(0, 60, 140),
            new SKColor(0, 160, 200),
            new SKColor(180, 240, 255));

        public static readonly ColorPalette Monochrome = new(
            new SKColor(0, 0, 0),
            new SKColor(50, 50, 50),
            new SKColor(150, 150, 150),
            new SKColor(255, 255, 255));

        private readonly SKColor[] _stops;

        public ColorPalette(params SKColor[] stops) => _stops = stops;

        public SKColor Sample(float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            if (_stops.Length == 1) return _stops[0];

            float scaled = t * (_stops.Length - 1);
            int lo = (int)scaled;
            int hi = Math.Min(lo + 1, _stops.Length - 1);
            float frac = scaled - lo;

            var a = _stops[lo];
            var b = _stops[hi];
            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * frac),
                (byte)(a.Green + (b.Green - a.Green) * frac),
                (byte)(a.Blue + (b.Blue - a.Blue) * frac));
        }
    }
}
