using System;
using SkiaSharp;

namespace Spectralis.App.Visualizers
{
    public class ChromaticVisualizer : SkiaVisualizerBase
    {
        public override string Id => "chromatic";
        public override string DisplayName => "Chromatic";
        public override string Category => "Spectrum";

        private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        protected override void RenderSkia(SKCanvas canvas, double width, double height)
        {
            canvas.Clear(new SKColor(8, 8, 16));
            if (Spectrum.Length == 0) return;

            float w = (float)width;
            float h = (float)height;
            int keys = 12;
            float keyW = w / keys;

            using var paint = new SKPaint { IsAntialias = true };
            using var textPaint = new SKPaint { IsAntialias = true, TextSize = 10f, Color = new SKColor(160, 160, 200) };

            for (int note = 0; note < keys; note++)
            {
                float energy = 0f;
                int bandStart = note * Spectrum.Length / keys;
                int bandEnd = Math.Min((note + 1) * Spectrum.Length / keys, Spectrum.Length);
                for (int b = bandStart; b < bandEnd; b++) energy += Spectrum[b];
                energy /= Math.Max(1, bandEnd - bandStart);

                float barH = energy * h * 0.9f;
                float x = note * keyW;
                float hue = note / (float)keys * 360f;
                bool isSharp = note == 1 || note == 3 || note == 6 || note == 8 || note == 10;

                paint.Color = isSharp
                    ? HsvToColor(hue, 1f, 0.7f, (byte)(100 + energy * 155))
                    : HsvToColor(hue, 0.7f, 0.95f, (byte)(100 + energy * 155));

                canvas.DrawRect(x + 1, h - barH, keyW - 2, barH, paint);
                canvas.DrawText(NoteNames[note], x + keyW / 2f - 6f, h - 4f, textPaint);
            }
        }
    }
}
