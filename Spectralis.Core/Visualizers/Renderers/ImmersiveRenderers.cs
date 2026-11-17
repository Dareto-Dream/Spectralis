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

public sealed class StardustRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var count = Detail(scene, 180, 340, 560);
        var time = scene.MotionTimeSeconds;
        var bass = Bass(scene);
        var vanishing = new Vector2(bounds.Width * .05f * MathF.Sin((float)(time * .08)), bounds.Height * .04f * MathF.Cos((float)(time * .11)));
        var energy = scene.MusicalEnergy;
        for (var i = 0; i < count; i++)
        {
            // Irrational strides distribute a stable field without a random generator or stored particle state.
            var angle = i * 2.399963f;
            // Three depth populations and curved currents give parallax without
            // rotating the whole camera or turning every kick into a warp jump.
            var layer = i % 3;
            var radius = (.45f + Fraction(i * .754877) * 6) * (1 + energy * .22f);
            var z = .55f + Fraction(i * .618034 - scene.TravelDistance * (.018 + layer * .018)) * 14;
            var drift = MathF.Sin(z * .38f + (float)time * .18f + layer) * (.15f + layer * .12f);
            var point = new Vector3(MathF.Cos(angle + drift) * radius, MathF.Sin(angle + drift) * radius * .7f, z);
            var start = Project(point, bounds) + vanishing;
            var trail = scene.Appearance.ReduceMotion ? .01f : .035f + layer * .05f + energy * .3f;
            var tail = Project(point with { Z = z + trail }, bounds) + vanishing;
            if (start.X < bounds.Left || start.X > bounds.Right || start.Y < bounds.Top || start.Y > bounds.Bottom) continue;
            var ripple = MathF.Exp(-MathF.Abs(radius - Fraction(time * .16) * 7) * 3) * scene.TransientEnvelope;
            var alpha = (int)Math.Clamp(35 + (1 - z / 14.6f) * 175 + ripple * 80, 0, 255);
            var size = Math.Clamp((1.8f + layer * .5f) / z, .45f, 3.2f);
            var shade = VizColor.Blend(Color(scene, Fraction(i * .381966)), VizColor.FromRgb(219,232,255), .4f+layer*.15f).WithAlpha(alpha);
            canvas.DrawLine(tail, start, shade, size, true);
            canvas.FillEllipse(new(start.X-size*.55f,start.Y-size*.55f,size*1.1f,size*1.1f),shade.WithAlpha(Math.Min(255,alpha+35)));
            if (z < 3.5f && i % 3 == 0)
                canvas.FillRadialGlow(new(start.X - size * 5, start.Y - size * 5, size * 10, size * 10), Color(scene, .1f, 38));
        }
        Finish(canvas, bounds, scene);
    }
}

public sealed class OrbitalRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var rings = Detail(scene, 30, 48, 68);
        var segments = Detail(scene, 80, 112, 144);
        Span<Vector2> points = stackalloc Vector2[145];
        var time = (float)scene.MotionTimeSeconds;
        var bass = Bass(scene);
        var scale = Math.Min(bounds.Width * .43f, bounds.Height * .72f);
        var centre = new Vector2(bounds.CenterX, bounds.CenterY + bounds.Height * .035f);
        var hole = scale * .22f;
        // 4.25 degrees above the accretion plane. The lensed far side rises
        // around the silhouette; the near side passes in front of it.
        const float inclination = 4.25f * MathF.PI / 180;
        canvas.FillRadialGlow(new(centre.X - scale, centre.Y - scale * .7f, scale * 2, scale * 1.4f), Color(scene, .3f, 38));
        for (var pass = 0; pass < 2; pass++)
        {
            if (pass == 1)
            {
                canvas.FillEllipse(new(centre.X-hole, centre.Y-hole, hole*2, hole*2), VizColor.FromRgb(1,2,5));
                canvas.DrawEllipse(new(centre.X-hole*1.03f, centre.Y-hole*1.03f, hole*2.06f, hole*2.06f), Color(scene, .12f, 160), 1.5f);
            }
            for (var ring = rings - 1; ring >= 0; ring--)
            {
                var depth = ring / (float)(rings - 1);
                var radius = hole * 1.12f + depth * (scale-hole*1.12f);
                for (var i = 0; i <= segments; i++)
                {
                    var a = i / (float)segments * MathF.PI + (pass == 0 ? MathF.PI : 0);
                    var x = MathF.Cos(a) * radius;
                    var y = MathF.Sin(a) * radius * MathF.Sin(inclination);
                    if (pass == 0)
                    {
                        var lens = MathF.Sqrt(Math.Max(0, hole * hole * 1.18f - x*x));
                        y -= lens * (1-depth*.36f);
                    }
                    var turbulence = MathF.Sin(a*11 + time*(1.8f-depth) + ring*.55f) * (1.2f+bass*2);
                    points[i] = centre + new Vector2(x, y + turbulence*(.3f+depth*.7f));
                }
                var alpha = (int)(45 + (1-depth)*140);
                canvas.DrawPolyline(points[..(segments+1)], Color(scene, depth, alpha), .8f + (1-depth)*1.1f);
                // Hot material rotates at a different speed at each radius.
                for (var arc = 0; arc < 3; arc++)
                {
                    var first = (int)(Fraction(time*(.08+(.12*(1-depth)))+arc/3.0+depth*.4) * (segments-10));
                    canvas.DrawPolyline(points.Slice(first, 8), VizColor.Blend(Color(scene, depth), VizColor.FromRgb(255,240,210), .55f).WithAlpha(alpha), 1.25f);
                }
            }
        }
        Finish(canvas, bounds, scene);
    }
}

public sealed class SilkRenderer : ImmersiveRenderer
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        if (bounds.Width < 2 || bounds.Height < 2) return;
        Space(canvas, bounds, scene);
        var strands = Detail(scene, 36, 64, 92);
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
                var x = (u - .5f) * (bounds.Width / bounds.Height) * 5.5f;
                var bank = strand % 3;
                var twist = u * 7.4f + time * (.26f + bank*.035f) + layer * 3.4f + bank*1.3f;
                var envelope = MathF.Sin(u * MathF.PI);
                var energy = SampleRange(scene.SpectrumLevels, i, samples + 1);
                var radius = .65f + layer * .95f + envelope * energy * .32f;
                var point = new Vector3(x, MathF.Sin(twist) * radius + (bank-1)*.35f + MathF.Sin(u*9-time*.2f)*bass*.16f,
                    3.5f + MathF.Cos(twist) * radius * .35f);
                points[i] = Project(point, bounds, .82f);
            }
            var color = Color(scene, layer, (int)(65 + (1 - layer) * 100));
            if (strand % 6 == 0 && scene.Appearance.Detail != VisualizerDetail.Low)
                canvas.DrawPolyline(points[..(samples + 1)], color.WithAlpha(18), 6);
            canvas.DrawPolyline(points[..(samples + 1)], color, 1.3f + layer*.7f);
        }
        Finish(canvas, bounds, scene);
    }
}
