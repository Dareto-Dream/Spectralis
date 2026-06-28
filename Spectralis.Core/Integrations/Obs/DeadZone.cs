using System.Text.Json.Serialization;

namespace Spectralis.Core.Integrations.Obs;

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
            for (var iter = 0; iter < 6; iter++)
            {
                var pushed = false;
                foreach (var dz in deadZones)
                {
                    if (!Overlaps(widget, dz)) continue;
                    PushAway(widget, dz);
                    pushed = true;
                }
                if (!pushed) break;
            }

            widget.X = Math.Clamp(widget.X, 0, Math.Max(0, 1 - widget.W));
            widget.Y = Math.Clamp(widget.Y, 0, Math.Max(0, 1 - widget.H));
        }

        return result;
    }

    private static bool Overlaps(ObsLayoutWidget w, DeadZone dz) =>
        dz.X < w.X + w.W && dz.X + dz.W > w.X &&
        dz.Y < w.Y + w.H && dz.Y + dz.H > w.Y;

    private static void PushAway(ObsLayoutWidget w, DeadZone dz)
    {
        double pushRight = (dz.X + dz.W) - w.X;
        double pushLeft  = (w.X + w.W)   - dz.X;
        double pushDown  = (dz.Y + dz.H) - w.Y;
        double pushUp    = (w.Y + w.H)   - dz.Y;

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
                var nx = w.X + c.dx;
                var ny = w.Y + c.dy;
                return nx >= 0 && nx + w.W <= 1 && ny >= 0 && ny + w.H <= 1;
            })
            .OrderBy(c => Math.Abs(c.dx) + Math.Abs(c.dy))
            .Cast<(double dx, double dy)?>()
            .FirstOrDefault()
            ?? ((double dx, double dy)?)candidates.OrderBy(c => Math.Abs(c.dx) + Math.Abs(c.dy)).First();

        w.X = Math.Clamp(w.X + best!.Value.dx, 0, Math.Max(0, 1 - w.W));
        w.Y = Math.Clamp(w.Y + best!.Value.dy, 0, Math.Max(0, 1 - w.H));
    }
}
