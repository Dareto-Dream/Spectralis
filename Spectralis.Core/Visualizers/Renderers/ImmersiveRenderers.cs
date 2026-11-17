using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

/// <summary>Perspective geometry drawn by the app compositor; no native readback or per-frame pixel buffers.</summary>
public abstract class ImmersiveRenderer : VisualizerRendererBase
{
    protected static int Detail(VisualizerScene scene, int low, int balanced, int high) => scene.Appearance.Detail switch
    {
        VisualizerDetail.Low => low,
        VisualizerDetail.High => high,
        _ => balanced,
    };

    protected static float Bass(VisualizerScene scene) => Math.Clamp(SampleRange(scene.SpectrumLevels, 0, 8), 0, 1);
    protected static float Fraction(double value) => (float)(value - Math.Floor(value));
    protected static VizColor Color(VisualizerScene scene, float mix, int alpha = 255) =>
        VizColor.Blend(scene.Theme.BarStartColor, scene.Theme.BarEndColor, mix).WithAlpha(alpha);

    protected static void Space(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        // Keep a dark adaptation field even with the application's light theme.
        canvas.FillRectGradientV(bounds, VizColor.FromRgb(8, 10, 18), VizColor.FromRgb(3, 4, 10));
        canvas.FillRadialGlow(new(bounds.CenterX - bounds.Width * .42f, bounds.CenterY - bounds.Height * .5f,
            bounds.Width * .84f, bounds.Height), Color(scene, .8f, 30));
    }

    protected static Vector2 Project(Vector3 point, VizRect bounds, float scale = .9f) =>
        new(bounds.CenterX + point.X / Math.Max(.15f, point.Z) * bounds.Height * scale,
            bounds.CenterY + point.Y / Math.Max(.15f, point.Z) * bounds.Height * scale);

    protected static void Finish(IVizCanvas canvas, VizRect bounds, VisualizerScene scene) => DrawHud(canvas, bounds, scene, darkSurface: true);
}

public sealed class WormholeRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var time = scene.MotionTimeSeconds;
        var bass = Bass(scene);
        var rings = Detail(scene, 24, 40, 56);
        var segments = Detail(scene, 48, 64, 80);
        Span<Vector2> points = stackalloc Vector2[81];
        Span<Vector2> previous = stackalloc Vector2[81];
        var travel = Fraction(time * .42);
        var center = new Vector2(bounds.CenterX, bounds.CenterY);
        var min = Math.Min(bounds.Width, bounds.Height);
        // Far to near: translucent rings and spars blend in the correct depth order.
        for (var ring = rings - 1; ring >= 0; ring--)
        {
            var z = .6f + (ring + 1 - travel) * .34f;
            var depth = ring / (float)rings;
            var radius = min * (1.02f + bass * .10f) / z;
            var bend = new Vector2(MathF.Sin(z * .23f + (float)(time * .16)), MathF.Cos(z * .19f + (float)(time * .11)))
                * min * .13f * MathF.Min(z * .18f, 1);
            var twist = z * .17f + (float)(time * .12);
            for (var i = 0; i <= segments; i++)
            {
                var angle = i / (float)segments * MathF.Tau;
                var energy = SampleRange(scene.SpectrumLevels, i % segments, segments);
                var ripple = 1 + MathF.Sin(angle * 3 + z * .6f - (float)time) * .035f + energy * .075f;
                points[i] = center + bend + new Vector2(MathF.Cos(angle + twist), MathF.Sin(angle + twist)) * radius * ripple;
            }
            var alpha = (int)(40 + (1 - depth) * 145);
            var shade = Color(scene, depth, alpha);
            if (ring % 3 == 0 && scene.Appearance.Detail != VisualizerDetail.Low)
                canvas.DrawPolyline(points[..(segments + 1)], shade.WithAlpha(alpha / 6), 6);
            canvas.DrawPolyline(points[..(segments + 1)], shade, 1 + (1 - depth) * .8f);
            if (ring < rings - 1)
                for (var i = 0; i < segments; i += 4)
                    canvas.DrawLine(previous[i], points[i], shade.WithAlpha(alpha / 3), .7f);
            points.CopyTo(previous);
        }
        Finish(canvas, bounds, scene);
    }
}

public sealed class StarfieldRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var count = Detail(scene, 160, 320, 560);
        var time = scene.MotionTimeSeconds;
        var bass = Bass(scene);
        var vanishing = new Vector2(bounds.Width * .07f * MathF.Sin((float)(time * .12)), bounds.Height * .05f * MathF.Cos((float)(time * .15)));
        for (var i = 0; i < count; i++)
        {
            // Irrational strides distribute a stable field without a random generator or stored particle state.
            var angle = i * 2.399963f;
            var radius = .25f + Fraction(i * .754877) * 5;
            var z = .3f + Fraction(i * .618034 - time * .09) * 12;
            var point = new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, z);
            var start = Project(point, bounds) + vanishing;
            var tail = Project(point with { Z = z + (scene.Appearance.ReduceMotion ? .015f : .12f + bass * .35f) }, bounds) + vanishing;
            if (start.X < bounds.Left || start.X > bounds.Right || start.Y < bounds.Top || start.Y > bounds.Bottom) continue;
            var alpha = (int)(40 + (1 - z / 12.3f) * 200);
            var size = Math.Clamp(2.8f / z, .6f, 3.2f);
            canvas.DrawLine(tail, start, Color(scene, Fraction(i * .381966), alpha), size, true);
            if (z < 2.5f)
                canvas.FillEllipse(new(start.X - size * 2, start.Y - size * 2, size * 4, size * 4), Color(scene, .1f, 20));
        }
        Finish(canvas, bounds, scene);
    }
}

public sealed class OrbitalRenderer : ImmersiveRenderer
{
    private readonly record struct OrbitPoint(Vector3 Position, float Mix, float Size);

    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var count = Detail(scene, 280, 520, 880);
        Span<OrbitPoint> particles = stackalloc OrbitPoint[880];
        var time = (float)scene.MotionTimeSeconds;
        var bass = Bass(scene);
        var yaw = .2f + MathF.Sin(time * .18f) * .24f;
        var pitch = -.85f + MathF.Sin(time * .13f) * .12f;
        for (var i = 0; i < count; i++)
        {
            var band = i % 5;
            var angle = i * 2.399963f + time * (.16f + band * .035f);
            var energy = SampleRange(scene.SpectrumLevels, i % 64, 64);
            var radius = 1.1f + band * .16f + energy * .12f;
            var position = new Vector3(MathF.Cos(angle) * radius,
                MathF.Sin(angle * 3 + band) * (.12f + energy * .30f), MathF.Sin(angle) * radius);
            position = VizMath.Rotate(position, yaw, pitch);
            position.Z += 4.2f;
            particles[i] = new(position, band / 4f, .8f + energy * 2.2f);
        }
        particles[..count].Sort(static (a, b) => b.Position.Z.CompareTo(a.Position.Z));
        var core = Math.Min(bounds.Width, bounds.Height) * (.10f + bass * .025f);
        canvas.FillRadialGlow(new(bounds.CenterX - core * 2, bounds.CenterY - core * 2, core * 4, core * 4), Color(scene, .5f, 65));
        for (var i = 0; i < count; i++)
        {
            var particle = particles[i];
            var point = Project(particle.Position, bounds, 1.05f);
            var depth = Math.Clamp((6 - particle.Position.Z) / 3.5f, 0, 1);
            var size = particle.Size * (.7f + depth) * Math.Clamp(bounds.Height / 720f, .35f, 1.5f);
            canvas.FillEllipse(new(point.X - size, point.Y - size, size * 2, size * 2), Color(scene, particle.Mix, (int)(50 + depth * 200)));
        }
        canvas.DrawEllipse(new(bounds.CenterX - core * .3f, bounds.CenterY - core * .3f, core * .6f, core * .6f), Color(scene, 0, 100), 1);
        Finish(canvas, bounds, scene);
    }
}

public sealed class SilkRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var strands = Detail(scene, 22, 38, 56);
        var samples = Detail(scene, 72, 112, 144);
        Span<Vector2> points = stackalloc Vector2[145];
        var time = (float)scene.MotionTimeSeconds;
        var bass = Bass(scene);
        for (var strand = strands - 1; strand >= 0; strand--)
        {
            var layer = strand / (float)(strands - 1);
            for (var i = 0; i <= samples; i++)
            {
                var u = i / (float)samples;
                var x = (u - .5f) * 5;
                var twist = x * 1.55f + time * .38f + layer * .95f;
                var envelope = MathF.Sin(u * MathF.PI);
                var energy = SampleRange(scene.SpectrumLevels, i, samples + 1);
                var radius = .48f + layer * .48f + envelope * energy * .28f;
                var point = new Vector3(x, MathF.Sin(twist) * radius + MathF.Sin(x * 2 - time * .2f) * bass * .12f,
                    4 + MathF.Cos(twist) * radius);
                points[i] = Project(point, bounds, .82f);
            }
            var color = Color(scene, layer, (int)(55 + (1 - layer) * 120));
            if (strand % 6 == 0 && scene.Appearance.Detail != VisualizerDetail.Low)
                canvas.DrawPolyline(points[..(samples + 1)], color.WithAlpha(18), 6);
            canvas.DrawPolyline(points[..(samples + 1)], color, 1.15f);
        }
        Finish(canvas, bounds, scene);
    }
}
