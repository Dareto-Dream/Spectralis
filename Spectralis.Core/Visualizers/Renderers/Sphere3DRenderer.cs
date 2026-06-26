using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class Sphere3DRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        Draw3DSphere(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void Draw3DSphere(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-26, -22);
        contentBounds = VizRect.FromLTRB(contentBounds.Left, contentBounds.Top + 18, contentBounds.Right, contentBounds.Bottom - 8);
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        var phase = scene.AnimationPhase * MathF.PI / 180f;
        var camera = new VizCamera3D(
            new Vector2(contentBounds.Left + (contentBounds.Width / 2f), contentBounds.Top + (contentBounds.Height / 2f)),
            Math.Min(contentBounds.Width, contentBounds.Height) * 0.34f,
            5.8f);
        var yaw = phase;
        var pitch = -0.36f + (MathF.Sin(phase) * 0.08f);
        const float radius = 1.32f;

        // Aura
        canvas.FillRadialGlow(
            new VizRect(
                camera.ScreenCenter.X - (contentBounds.Width * 0.24f),
                camera.ScreenCenter.Y - (contentBounds.Height * 0.22f),
                contentBounds.Width * 0.48f,
                contentBounds.Height * 0.44f),
            scene.Theme.AmbientGlowColor.WithAlpha(62));

        DrawSphereRings(canvas, scene, camera, yaw, pitch, radius);
        DrawSpectrumSpikes(canvas, scene, camera, yaw, pitch, radius, phase);

        var coreRadius = Math.Min(contentBounds.Width, contentBounds.Height) * 0.10f;
        canvas.FillEllipse(
            new VizRect(camera.ScreenCenter.X - coreRadius, camera.ScreenCenter.Y - coreRadius, coreRadius * 2f, coreRadius * 2f),
            scene.Theme.AmbientGlowColor.WithAlpha(68));
    }

    private static void DrawSphereRings(IVizCanvas canvas, VisualizerScene scene, VizCamera3D camera, float yaw, float pitch, float radius)
    {
        var latitudes = new[] { -0.95f, -0.48f, 0f, 0.48f, 0.95f };
        foreach (var latitude in latitudes)
        {
            var ringPoints = BuildRingPoints(camera, yaw, pitch, radius, latitude, horizontal: true);
            DrawDepthPolyline(canvas, ringPoints, scene.Theme.AmbientGridColor, scene.Theme.BarStartColor, 1.25f);
        }

        for (var longitudeIndex = 0; longitudeIndex < 7; longitudeIndex++)
        {
            var longitude = longitudeIndex * (MathF.PI / 3.5f);
            var ringPoints = BuildRingPoints(camera, yaw, pitch, radius, longitude, horizontal: false);
            DrawDepthPolyline(canvas, ringPoints, scene.Theme.RingColor, scene.Theme.HudLabelColor, 1f);
        }
    }

    private static void DrawSpectrumSpikes(
        IVizCanvas canvas,
        VisualizerScene scene,
        VizCamera3D camera,
        float yaw,
        float pitch,
        float radius,
        float phase)
    {
        var spikes = new List<SphereSpike>(40);
        const int bars = 36;

        for (var index = 0; index < bars; index++)
        {
            var longitude = index / (float)bars * MathF.PI * 2f;
            var waveform = scene.WaveformPoints[(index * scene.WaveformPoints.Length) / bars];
            var latitude = (MathF.Sin((longitude * 2f) + phase) * 0.34f) + (waveform * 0.18f);
            var normal = CreateSphereNormal(longitude, latitude);
            var energy = SampleRange(scene.SpectrumLevels, index, bars);
            var peak = SampleRange(scene.PeakHoldLevels, index, bars);
            var spikeLength = 0.18f + (energy * 0.95f) + (Math.Max(0f, waveform) * 0.14f);
            var peakLength = 0.18f + (peak * 0.95f) + (Math.Max(0f, waveform) * 0.14f);

            var startView = VizMath.Rotate(normal * radius, yaw, pitch);
            var endView = VizMath.Rotate(normal * (radius + spikeLength), yaw, pitch);
            var peakView = VizMath.Rotate(normal * (radius + peakLength), yaw, pitch);
            var start = camera.Project(startView);
            var end = camera.Project(endView);
            var peakPoint = camera.Project(peakView);
            var alpha = endView.Z > 0.42f ? 52 : 188;

            spikes.Add(new SphereSpike(start, end, peakPoint, energy, alpha));
        }

        foreach (var spike in spikes.OrderByDescending(static spike => (spike.Start.Depth + spike.End.Depth) / 2f))
        {
            canvas.DrawLine(
                spike.Start.ScreenPoint, spike.End.ScreenPoint,
                scene.Theme.BarGlowColor.WithAlpha(Math.Min(255, (spike.Alpha / 2) + 24)), 5.6f, roundCap: true);
            canvas.DrawLine(
                spike.Start.ScreenPoint, spike.End.ScreenPoint,
                scene.Theme.BarEndColor.WithAlpha(spike.Alpha), 2f, roundCap: true);

            if (!scene.ShowPeaks)
                continue;

            canvas.FillEllipse(
                new VizRect(spike.Peak.ScreenPoint.X - 3f, spike.Peak.ScreenPoint.Y - 3f, 6f, 6f),
                scene.Theme.PeakColor.WithAlpha(Math.Min(255, spike.Alpha + 18)));
        }
    }

    private static VizProjectedPoint[] BuildRingPoints(VizCamera3D camera, float yaw, float pitch, float radius, float ringValue, bool horizontal)
    {
        const int samples = 72;
        var points = new VizProjectedPoint[samples + 1];

        for (var index = 0; index <= samples; index++)
        {
            var angle = index / (float)samples * MathF.PI * 2f;
            var normal = horizontal
                ? CreateSphereNormal(angle, ringValue)
                : CreateSphereNormal(ringValue, MathF.Sin(angle) * 0.5f);

            points[index] = camera.Project(VizMath.Rotate(normal * radius, yaw, pitch));
        }

        return points;
    }

    private static Vector3 CreateSphereNormal(float longitude, float latitude)
    {
        var cosLatitude = MathF.Cos(latitude);
        return Vector3.Normalize(new Vector3(
            MathF.Cos(longitude) * cosLatitude,
            MathF.Sin(latitude),
            MathF.Sin(longitude) * cosLatitude));
    }

    private static void DrawDepthPolyline(IVizCanvas canvas, VizProjectedPoint[] points, VizColor backColor, VizColor frontColor, float width)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var depth = (points[index - 1].Depth + points[index].Depth) / 2f;
            var color = depth > 0.35f
                ? backColor.WithAlpha(34)
                : frontColor.WithAlpha(120);

            canvas.DrawLine(points[index - 1].ScreenPoint, points[index].ScreenPoint, color, width, roundCap: true);
        }
    }

    private readonly record struct SphereSpike(
        VizProjectedPoint Start,
        VizProjectedPoint End,
        VizProjectedPoint Peak,
        float Energy,
        int Alpha);
}
