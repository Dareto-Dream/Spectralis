using System.Numerics;

namespace Spectralis.Core.Visualizers;

public readonly record struct VizProjectedPoint(Vector2 ScreenPoint, float Depth, float Perspective);

public readonly record struct VizCamera3D(Vector2 ScreenCenter, float Scale, float CameraDistance)
{
    public VizProjectedPoint Project(Vector3 viewPoint)
    {
        var depth = viewPoint.Z + CameraDistance;
        var perspective = CameraDistance / MathF.Max(0.35f, depth);

        return new VizProjectedPoint(
            new Vector2(
                ScreenCenter.X + (viewPoint.X * Scale * perspective),
                ScreenCenter.Y - (viewPoint.Y * Scale * perspective)),
            viewPoint.Z,
            perspective);
    }
}

public static class VizMath
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

    /// <summary>Averages a sub-range of <paramref name="source"/> for display bar <paramref name="index"/>.</summary>
    public static float SampleRange(float[] source, int index, int displayBars)
    {
        if (source.Length == 0)
            return 0;

        var start = index * source.Length / displayBars;
        var end = Math.Max(start + 1, ((index + 1) * source.Length) / displayBars);
        float total = 0;

        for (var position = start; position < end; position++)
            total += source[Math.Min(position, source.Length - 1)];

        return total / (end - start);
    }

    /// <summary>
    /// Expands control points into a cardinal spline polyline — the replacement for
    /// GDI+ Graphics.AddCurve(points, tension). Returns segmentsPerSpan points per span.
    /// </summary>
    public static Vector2[] CardinalSpline(ReadOnlySpan<Vector2> points, float tension, int segmentsPerSpan = 8)
    {
        if (points.Length < 2)
            return points.ToArray();

        // GDI tension t maps to cardinal coefficient c = t (0 = straight lines, 1 = very loose).
        var c = tension;
        var result = new List<Vector2>((points.Length - 1) * segmentsPerSpan + 1) { points[0] };

        for (var i = 0; i < points.Length - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(points.Length - 1, i + 2)];

            var m1 = (p2 - p0) * c;
            var m2 = (p3 - p1) * c;

            for (var step = 1; step <= segmentsPerSpan; step++)
            {
                var t = step / (float)segmentsPerSpan;
                var t2 = t * t;
                var t3 = t2 * t;

                var h00 = (2 * t3) - (3 * t2) + 1;
                var h10 = t3 - (2 * t2) + t;
                var h01 = (-2 * t3) + (3 * t2);
                var h11 = t3 - t2;

                result.Add((p1 * h00) + (m1 * h10) + (p2 * h01) + (m2 * h11));
            }
        }

        return result.ToArray();
    }
}
