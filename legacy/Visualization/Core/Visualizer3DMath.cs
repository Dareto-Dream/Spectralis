using System.Drawing;
using System.Numerics;

namespace Spectralis;

internal readonly record struct VisualizerProjectedPoint(PointF ScreenPoint, float Depth, float Perspective);

internal readonly record struct VisualizerCamera3D(PointF ScreenCenter, float Scale, float CameraDistance)
{
    public VisualizerProjectedPoint Project(Vector3 viewPoint)
    {
        var depth = viewPoint.Z + CameraDistance;
        var perspective = CameraDistance / MathF.Max(0.35f, depth);

        return new VisualizerProjectedPoint(
            new PointF(
                ScreenCenter.X + (viewPoint.X * Scale * perspective),
                ScreenCenter.Y - (viewPoint.Y * Scale * perspective)),
            viewPoint.Z,
            perspective);
    }
}

internal static class Visualizer3DMath
{
    public static Vector3 Rotate(Vector3 point, float yaw, float pitch, float roll = 0f) =>
        Vector3.Transform(point, Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll));

    public static bool IsFaceVisible(IReadOnlyList<Vector3> viewVertices)
    {
        if (viewVertices.Count < 3)
            return false;

        var edge1 = viewVertices[1] - viewVertices[0];
        var edge2 = viewVertices[2] - viewVertices[0];
        var normal = Vector3.Cross(edge1, edge2);
        return normal.Z > 0.0001f;
    }
}
