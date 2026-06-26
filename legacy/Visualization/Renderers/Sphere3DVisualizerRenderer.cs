using System.Drawing;
using System.Drawing.Drawing2D;
using System.Numerics;

namespace Spectralis;

internal sealed class Sphere3DVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        Draw3DSphere(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void Draw3DSphere(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -26, -22);
        contentBounds = Rectangle.FromLTRB(contentBounds.Left, contentBounds.Top + 18, contentBounds.Right, contentBounds.Bottom - 8);
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        var phase = scene.AnimationPhase * MathF.PI / 180f;
        var camera = new VisualizerCamera3D(
            new PointF(contentBounds.Left + (contentBounds.Width / 2f), contentBounds.Top + (contentBounds.Height / 2f)),
            Math.Min(contentBounds.Width, contentBounds.Height) * 0.34f,
            5.8f);
        var yaw = phase;
        var pitch = -0.36f + (MathF.Sin(phase) * 0.08f);
        var radius = 1.32f;

        DrawSphereAura(graphics, camera.ScreenCenter, contentBounds, scene);
        DrawSphereRings(graphics, scene, camera, yaw, pitch, radius);
        DrawSpectrumSpikes(graphics, scene, camera, yaw, pitch, radius, phase);

        using var coreBrush = new SolidBrush(Color.FromArgb(68, scene.Theme.AmbientGlowColor));
        var coreRadius = Math.Min(contentBounds.Width, contentBounds.Height) * 0.10f;
        graphics.FillEllipse(coreBrush, camera.ScreenCenter.X - coreRadius, camera.ScreenCenter.Y - coreRadius, coreRadius * 2f, coreRadius * 2f);
    }

    private static void DrawSphereAura(Graphics graphics, PointF center, Rectangle contentBounds, VisualizerScene scene)
    {
        using var auraBrush = new PathGradientBrush(
            [
                new PointF(center.X, center.Y - (contentBounds.Height * 0.24f)),
                new PointF(center.X + (contentBounds.Width * 0.28f), center.Y),
                new PointF(center.X, center.Y + (contentBounds.Height * 0.24f)),
                new PointF(center.X - (contentBounds.Width * 0.28f), center.Y)
            ])
        {
            CenterColor = Color.FromArgb(62, scene.Theme.AmbientGlowColor),
            SurroundColors =
            [
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor),
                Color.FromArgb(0, scene.Theme.AmbientGlowColor)
            ]
        };
        graphics.FillEllipse(
            auraBrush,
            center.X - (contentBounds.Width * 0.24f),
            center.Y - (contentBounds.Height * 0.22f),
            contentBounds.Width * 0.48f,
            contentBounds.Height * 0.44f);
    }

    private static void DrawSphereRings(Graphics graphics, VisualizerScene scene, VisualizerCamera3D camera, float yaw, float pitch, float radius)
    {
        var latitudes = new[] { -0.95f, -0.48f, 0f, 0.48f, 0.95f };
        foreach (var latitude in latitudes)
        {
            var ringPoints = BuildRingPoints(camera, yaw, pitch, radius, latitude, horizontal: true);
            DrawDepthPolyline(graphics, ringPoints, scene.Theme.AmbientGridColor, scene.Theme.BarStartColor, 1.25f);
        }

        for (var longitudeIndex = 0; longitudeIndex < 7; longitudeIndex++)
        {
            var longitude = longitudeIndex * (MathF.PI / 3.5f);
            var ringPoints = BuildRingPoints(camera, yaw, pitch, radius, longitude, horizontal: false);
            DrawDepthPolyline(graphics, ringPoints, scene.Theme.RingColor, scene.Theme.HudLabelColor, 1f);
        }
    }

    private static void DrawSpectrumSpikes(
        Graphics graphics,
        VisualizerScene scene,
        VisualizerCamera3D camera,
        float yaw,
        float pitch,
        float radius,
        float phase)
    {
        var spikes = new List<SphereSpike>(40);
        var bars = 36;

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

            var startView = Visualizer3DMath.Rotate(normal * radius, yaw, pitch);
            var endView = Visualizer3DMath.Rotate(normal * (radius + spikeLength), yaw, pitch);
            var peakView = Visualizer3DMath.Rotate(normal * (radius + peakLength), yaw, pitch);
            var start = camera.Project(startView);
            var end = camera.Project(endView);
            var peakPoint = camera.Project(peakView);
            var alpha = endView.Z > 0.42f ? 52 : 188;

            spikes.Add(new SphereSpike(start, end, peakPoint, energy, alpha));
        }

        foreach (var spike in spikes.OrderByDescending(static spike => (spike.Start.Depth + spike.End.Depth) / 2f))
        {
            using var glowPen = new Pen(Color.FromArgb(Math.Min(255, spike.Alpha / 2 + 24), scene.Theme.BarGlowColor), 5.6f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            using var barPen = new Pen(Color.FromArgb(spike.Alpha, scene.Theme.BarEndColor), 2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };

            graphics.DrawLine(glowPen, spike.Start.ScreenPoint, spike.End.ScreenPoint);
            graphics.DrawLine(barPen, spike.Start.ScreenPoint, spike.End.ScreenPoint);

            if (!scene.ShowPeaks)
                continue;

            using var peakBrush = new SolidBrush(Color.FromArgb(Math.Min(255, spike.Alpha + 18), scene.Theme.PeakColor));
            graphics.FillEllipse(peakBrush, spike.Peak.ScreenPoint.X - 3f, spike.Peak.ScreenPoint.Y - 3f, 6f, 6f);
        }
    }

    private static VisualizerProjectedPoint[] BuildRingPoints(VisualizerCamera3D camera, float yaw, float pitch, float radius, float ringValue, bool horizontal)
    {
        var samples = 72;
        var points = new VisualizerProjectedPoint[samples + 1];

        for (var index = 0; index <= samples; index++)
        {
            var angle = index / (float)samples * MathF.PI * 2f;
            var normal = horizontal
                ? CreateSphereNormal(angle, ringValue)
                : CreateSphereNormal(ringValue, MathF.Sin(angle) * 0.5f);

            points[index] = camera.Project(Visualizer3DMath.Rotate(normal * radius, yaw, pitch));
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

    private static void DrawDepthPolyline(Graphics graphics, VisualizerProjectedPoint[] points, Color backColor, Color frontColor, float width)
    {
        for (var index = 1; index < points.Length; index++)
        {
            var depth = (points[index - 1].Depth + points[index].Depth) / 2f;
            var color = depth > 0.35f
                ? Color.FromArgb(34, backColor)
                : Color.FromArgb(120, frontColor);

            using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawLine(pen, points[index - 1].ScreenPoint, points[index].ScreenPoint);
        }
    }

    private readonly record struct SphereSpike(
        VisualizerProjectedPoint Start,
        VisualizerProjectedPoint End,
        VisualizerProjectedPoint Peak,
        float Energy,
        int Alpha);
}
