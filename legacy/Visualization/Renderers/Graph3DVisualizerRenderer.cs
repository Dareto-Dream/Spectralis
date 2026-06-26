using System.Drawing;
using System.Numerics;

namespace Spectralis;

internal sealed class Graph3DVisualizerRenderer : VisualizerRendererBase
{
    public override void Draw(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        DrawBackground(graphics, bounds, scene);
        Draw3DGraph(graphics, bounds, scene);
        DrawHud(graphics, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(graphics, bounds, scene);
    }

    private static void Draw3DGraph(Graphics graphics, Rectangle bounds, VisualizerScene scene)
    {
        var contentBounds = Rectangle.Inflate(bounds, -28, -24);
        contentBounds = Rectangle.FromLTRB(contentBounds.Left, contentBounds.Top + 24, contentBounds.Right, contentBounds.Bottom - 8);
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        var phase = scene.AnimationPhase * MathF.PI / 180f;
        var camera = new VisualizerCamera3D(
            new PointF(contentBounds.Left + (contentBounds.Width / 2f), contentBounds.Top + (contentBounds.Height * 0.86f)),
            Math.Min(contentBounds.Width, contentBounds.Height) * 0.58f,
            6.8f);
        var yaw = -0.72f;
        var pitch = -0.56f + (MathF.Sin(phase) * 0.04f);
        var columns = 14;
        var rows = 7;
        var worldWidth = 3.8f;
        var worldDepth = 2.8f;
        var spacingX = worldWidth / columns;
        var spacingZ = worldDepth / rows;
        var barWidth = spacingX * 0.74f;
        var barDepth = spacingZ * 0.72f;
        var faces = new List<GraphFace>(columns * rows * 3);

        DrawFloorGrid(graphics, scene, camera, yaw, pitch, columns, rows, worldWidth, worldDepth);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = -worldWidth / 2f + (spacingX * (column + 0.5f));
                var z = 0.28f + (spacingZ * row);
                var height = ComputeBarHeight(scene, column, row, columns, rows, phase);
                AddBarFaces(faces, scene, camera, yaw, pitch, x, z, barWidth, barDepth, height);
            }
        }

        foreach (var face in faces.OrderByDescending(static face => face.Depth))
        {
            using var fillBrush = new SolidBrush(face.FillColor);
            using var outlinePen = new Pen(face.OutlineColor, 1.1f);
            graphics.FillPolygon(fillBrush, face.Points);
            graphics.DrawPolygon(outlinePen, face.Points);
        }
    }

    private static void DrawFloorGrid(
        Graphics graphics,
        VisualizerScene scene,
        VisualizerCamera3D camera,
        float yaw,
        float pitch,
        int columns,
        int rows,
        float worldWidth,
        float worldDepth)
    {
        using var gridPen = new Pen(Color.FromArgb(30, scene.Theme.AmbientGridColor), 1f);

        for (var row = 0; row <= rows; row++)
        {
            var z = 0.28f + ((worldDepth / rows) * row);
            var start = Project(new Vector3(-worldWidth / 2f, 0f, z), camera, yaw, pitch);
            var end = Project(new Vector3(worldWidth / 2f, 0f, z), camera, yaw, pitch);
            graphics.DrawLine(gridPen, start.ScreenPoint, end.ScreenPoint);
        }

        for (var column = 0; column <= columns; column++)
        {
            var x = -worldWidth / 2f + ((worldWidth / columns) * column);
            var start = Project(new Vector3(x, 0f, 0.28f), camera, yaw, pitch);
            var end = Project(new Vector3(x, 0f, 0.28f + worldDepth), camera, yaw, pitch);
            graphics.DrawLine(gridPen, start.ScreenPoint, end.ScreenPoint);
        }
    }

    private static float ComputeBarHeight(VisualizerScene scene, int column, int row, int columns, int rows, float phase)
    {
        var frontEnergy = SampleRange(scene.SpectrumLevels, column, columns);
        var phaseScroll = scene.PlaybackTimeSeconds * 8f;
        var depthBase = (column + (row * 3) + (int)phaseScroll) % scene.SpectrumLevels.Length;
        var depthNext = (depthBase + 1) % scene.SpectrumLevels.Length;
        var frac = phaseScroll % 1f;
        var tailEnergy = scene.SpectrumLevels[depthBase] + ((scene.SpectrumLevels[depthNext] - scene.SpectrumLevels[depthBase]) * frac);
        var waveIndex = (column * scene.WaveformPoints.Length) / columns;
        var wave = scene.WaveformPoints[Math.Clamp(waveIndex, 0, scene.WaveformPoints.Length - 1)];
        var travel = 0.5f + (0.5f * MathF.Sin((row * 0.82f) - (phase * 2f) + (column * 0.21f)));
        var depthBoost = 1f - (row / (float)(rows + 2));
        var energy = (frontEnergy * 0.72f) + (tailEnergy * 0.28f * travel);

        return 0.08f + (energy * 1.95f * depthBoost) + (Math.Max(0f, wave) * 0.24f) + (travel * 0.14f * Math.Max(0.2f, scene.RmsLevel));
    }

    private static void AddBarFaces(
        List<GraphFace> faces,
        VisualizerScene scene,
        VisualizerCamera3D camera,
        float yaw,
        float pitch,
        float x,
        float z,
        float width,
        float depth,
        float height)
    {
        var x0 = x - (width / 2f);
        var x1 = x + (width / 2f);
        var z0 = z - (depth / 2f);
        var z1 = z + (depth / 2f);

        var v000 = new Vector3(x0, 0f, z0);
        var v100 = new Vector3(x1, 0f, z0);
        var v110 = new Vector3(x1, 0f, z1);
        var v010 = new Vector3(x0, 0f, z1);
        var v001 = new Vector3(x0, height, z0);
        var v101 = new Vector3(x1, height, z0);
        var v111 = new Vector3(x1, height, z1);
        var v011 = new Vector3(x0, height, z1);

        var topColor = Tint(scene.Theme.BarStartColor, scene.Theme.PeakColor, 0.46f, 148);
        var sideColor = Tint(scene.Theme.BarEndColor, scene.Theme.BackgroundBottomColor, 0.22f, 118);
        var frontColor = Tint(scene.Theme.BarStartColor, scene.Theme.BarEndColor, 0.58f, 132);
        var outlineColor = Color.FromArgb(92, scene.Theme.HudLabelColor);

        AddFace(faces, camera, yaw, pitch, [v001, v101, v111, v011], topColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v000, v100, v101, v001], frontColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v100, v110, v111, v101], sideColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v010, v000, v001, v011], Color.FromArgb(80, sideColor), outlineColor);
    }

    private static void AddFace(
        List<GraphFace> faces,
        VisualizerCamera3D camera,
        float yaw,
        float pitch,
        Vector3[] worldVertices,
        Color fillColor,
        Color outlineColor)
    {
        var viewVertices = worldVertices
            .Select(vertex => Visualizer3DMath.Rotate(vertex, yaw, pitch))
            .ToArray();

        if (!Visualizer3DMath.IsFaceVisible(viewVertices))
            return;

        var projected = viewVertices
            .Select(camera.Project)
            .ToArray();

        faces.Add(new GraphFace(
            projected.Select(static point => point.ScreenPoint).ToArray(),
            projected.Average(static point => point.Depth),
            fillColor,
            outlineColor));
    }

    private static VisualizerProjectedPoint Project(Vector3 point, VisualizerCamera3D camera, float yaw, float pitch) =>
        camera.Project(Visualizer3DMath.Rotate(point, yaw, pitch));

    private static Color Tint(Color start, Color end, float mix, int alpha)
    {
        var amount = Math.Clamp(mix, 0f, 1f);
        return Color.FromArgb(
            alpha,
            (int)(start.R + ((end.R - start.R) * amount)),
            (int)(start.G + ((end.G - start.G) * amount)),
            (int)(start.B + ((end.B - start.B) * amount)));
    }

    private readonly record struct GraphFace(PointF[] Points, float Depth, Color FillColor, Color OutlineColor);
}
