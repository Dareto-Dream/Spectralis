using System.Numerics;

namespace Spectralis.Core.Visualizers.Renderers;

public sealed class Graph3DRenderer : VisualizerRendererBase
{
    public override void Draw(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        DrawBackground(canvas, bounds, scene);
        Draw3DGraph(canvas, bounds, scene);
        DrawHud(canvas, bounds, scene);

        if (IsNearSilence(scene))
            DrawPlaceholder(canvas, bounds, scene);
    }

    private static void Draw3DGraph(IVizCanvas canvas, VizRect bounds, VisualizerScene scene)
    {
        var contentBounds = bounds.Inflate(-28, -24);
        contentBounds = VizRect.FromLTRB(contentBounds.Left, contentBounds.Top + 24, contentBounds.Right, contentBounds.Bottom - 8);
        if (contentBounds.Width <= 0 || contentBounds.Height <= 0)
            return;

        var phase = scene.AnimationPhase * MathF.PI / 180f;
        var camera = new VizCamera3D(
            new Vector2(contentBounds.Left + (contentBounds.Width / 2f), contentBounds.Top + (contentBounds.Height * 0.86f)),
            Math.Min(contentBounds.Width, contentBounds.Height) * 0.58f,
            6.8f);
        var yaw = -0.72f;
        var pitch = -0.56f + (MathF.Sin(phase) * 0.04f);
        const int columns = 14;
        const int rows = 7;
        const float worldWidth = 3.8f;
        const float worldDepth = 2.8f;
        const float spacingX = worldWidth / columns;
        const float spacingZ = worldDepth / rows;
        const float barWidth = spacingX * 0.74f;
        const float barDepth = spacingZ * 0.72f;
        var faces = new List<GraphFace>(columns * rows * 3);

        DrawFloorGrid(canvas, scene, camera, yaw, pitch, columns, rows, worldWidth, worldDepth);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var x = (-worldWidth / 2f) + (spacingX * (column + 0.5f));
                var z = 0.28f + (spacingZ * row);
                var height = ComputeBarHeight(scene, column, row, columns, rows, phase);
                AddBarFaces(faces, scene, camera, yaw, pitch, x, z, barWidth, barDepth, height);
            }
        }

        foreach (var face in faces.OrderByDescending(static face => face.Depth))
        {
            canvas.FillPolygon(face.Points, face.FillColor);
            canvas.DrawPolygon(face.Points, face.OutlineColor, 1.1f);
        }
    }

    private static void DrawFloorGrid(
        IVizCanvas canvas,
        VisualizerScene scene,
        VizCamera3D camera,
        float yaw,
        float pitch,
        int columns,
        int rows,
        float worldWidth,
        float worldDepth)
    {
        var gridColor = scene.Theme.AmbientGridColor.WithAlpha(30);

        for (var row = 0; row <= rows; row++)
        {
            var z = 0.28f + ((worldDepth / rows) * row);
            var start = Project(new Vector3(-worldWidth / 2f, 0f, z), camera, yaw, pitch);
            var end = Project(new Vector3(worldWidth / 2f, 0f, z), camera, yaw, pitch);
            canvas.DrawLine(start.ScreenPoint, end.ScreenPoint, gridColor, 1f);
        }

        for (var column = 0; column <= columns; column++)
        {
            var x = (-worldWidth / 2f) + ((worldWidth / columns) * column);
            var start = Project(new Vector3(x, 0f, 0.28f), camera, yaw, pitch);
            var end = Project(new Vector3(x, 0f, 0.28f + worldDepth), camera, yaw, pitch);
            canvas.DrawLine(start.ScreenPoint, end.ScreenPoint, gridColor, 1f);
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
        VizCamera3D camera,
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
        var outlineColor = scene.Theme.HudLabelColor.WithAlpha(92);

        AddFace(faces, camera, yaw, pitch, [v001, v101, v111, v011], topColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v000, v100, v101, v001], frontColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v100, v110, v111, v101], sideColor, outlineColor);
        AddFace(faces, camera, yaw, pitch, [v010, v000, v001, v011], sideColor.WithAlpha(80), outlineColor);
    }

    private static void AddFace(
        List<GraphFace> faces,
        VizCamera3D camera,
        float yaw,
        float pitch,
        Vector3[] worldVertices,
        VizColor fillColor,
        VizColor outlineColor)
    {
        var viewVertices = worldVertices
            .Select(vertex => VizMath.Rotate(vertex, yaw, pitch))
            .ToArray();

        if (!VizMath.IsFaceVisible(viewVertices))
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

    private static VizProjectedPoint Project(Vector3 point, VizCamera3D camera, float yaw, float pitch) =>
        camera.Project(VizMath.Rotate(point, yaw, pitch));

    private static VizColor Tint(VizColor start, VizColor end, float mix, int alpha) =>
        VizColor.Blend(start, end, mix).WithAlpha(alpha);

    private readonly record struct GraphFace(Vector2[] Points, float Depth, VizColor FillColor, VizColor OutlineColor);
}
