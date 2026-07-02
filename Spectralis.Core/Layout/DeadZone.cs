using System.Text.Json.Serialization;
using Spectralis.Core.Integrations.Obs;

namespace Spectralis.Core.Layout;

/// <summary>
/// A normalized (0-1) rectangle the user has marked as off-limits for positioned UI.
/// Applies app-wide: OBS overlay widgets, in-app floating overlays (P2W banner,
/// clipboard toast), and visualizer HUD elements all avoid the same set of zones.
/// </summary>
public sealed class DeadZone
{
    [JsonPropertyName("x")] public double X { get; set; }
    [JsonPropertyName("y")] public double Y { get; set; }
    [JsonPropertyName("w")] public double W { get; set; }
    [JsonPropertyName("h")] public double H { get; set; }
    [JsonPropertyName("label")] public string? Label { get; set; }

    public DeadZone Clone() => (DeadZone)MemberwiseClone();
}

public static class DeadZoneHelper
{
    public static ObsLayout ApplyDeadZones(ObsLayout layout, IReadOnlyList<DeadZone> deadZones)
    {
        if (deadZones.Count == 0) return layout;

        var result = new ObsLayout
        {
            AllowFallback = layout.AllowFallback,
            Widgets = layout.Widgets.Select(w => w.Clone()).ToList()
        };

        foreach (var widget in result.Widgets)
        {
            var (x, y) = Resolve(widget.X, widget.Y, widget.W, widget.H, deadZones);
            widget.X = x;
            widget.Y = y;
        }

        return result;
    }

    /// <summary>
    /// Pushes a normalized (0-1) rectangle out of any overlapping dead zones and clamps it
    /// back into [0,1]. Used for OBS widgets as well as any other positioned app UI
    /// (floating overlays, visualizer HUD elements) that shares the same dead-zone set.
    /// </summary>
    public static (double X, double Y) Resolve(double x, double y, double w, double h, IReadOnlyList<DeadZone> deadZones)
    {
        if (deadZones.Count == 0) return (x, y);

        for (var iter = 0; iter < 6; iter++)
        {
            var pushed = false;
            foreach (var dz in deadZones)
            {
                if (!Overlaps(x, y, w, h, dz)) continue;
                (x, y) = PushAway(x, y, w, h, dz);
                pushed = true;
            }
            if (!pushed) break;
        }

        x = Math.Clamp(x, 0, Math.Max(0, 1 - w));
        y = Math.Clamp(y, 0, Math.Max(0, 1 - h));
        return (x, y);
    }

    private static bool Overlaps(double x, double y, double w, double h, DeadZone dz) =>
        dz.X < x + w && dz.X + dz.W > x &&
        dz.Y < y + h && dz.Y + dz.H > y;

    private static (double X, double Y) PushAway(double x, double y, double w, double h, DeadZone dz)
    {
        double pushRight = (dz.X + dz.W) - x;
        double pushLeft  = (x + w)       - dz.X;
        double pushDown  = (dz.Y + dz.H) - y;
        double pushUp    = (y + h)       - dz.Y;

        (double dx, double dy)[] candidates =
        [
            ( pushRight,    0),
            (-pushLeft,     0),
            (0,  pushDown),
            (0, -pushUp),
        ];

        var best = candidates
            .Where(c =>
            {
                var nx = x + c.dx;
                var ny = y + c.dy;
                return nx >= 0 && nx + w <= 1 && ny >= 0 && ny + h <= 1;
            })
            .OrderBy(c => Math.Abs(c.dx) + Math.Abs(c.dy))
            .Cast<(double dx, double dy)?>()
            .FirstOrDefault()
            ?? ((double dx, double dy)?)candidates.OrderBy(c => Math.Abs(c.dx) + Math.Abs(c.dy)).First();

        return (
            Math.Clamp(x + best!.Value.dx, 0, Math.Max(0, 1 - w)),
            Math.Clamp(y + best!.Value.dy, 0, Math.Max(0, 1 - h))
        );
    }
}
