using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Spectralis.App.Controls;

/// <summary>Thin strip of chapter-boundary ticks under the scrubber, shown in Podcast Mode.</summary>
public sealed class ChapterMarkersOverlay : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> BoundarySecondsProperty =
        AvaloniaProperty.Register<ChapterMarkersOverlay, IReadOnlyList<double>?>(nameof(BoundarySeconds));

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<ChapterMarkersOverlay, double>(nameof(DurationSeconds));

    public static readonly StyledProperty<int> ActiveIndexProperty =
        AvaloniaProperty.Register<ChapterMarkersOverlay, int>(nameof(ActiveIndex), -1);

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<ChapterMarkersOverlay, IBrush?>(nameof(TickBrush));

    static ChapterMarkersOverlay()
    {
        AffectsRender<ChapterMarkersOverlay>(
            BoundarySecondsProperty, DurationSecondsProperty, ActiveIndexProperty, TickBrushProperty);
    }

    public IReadOnlyList<double>? BoundarySeconds
    {
        get => GetValue(BoundarySecondsProperty);
        set => SetValue(BoundarySecondsProperty, value);
    }

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public int ActiveIndex
    {
        get => GetValue(ActiveIndexProperty);
        set => SetValue(ActiveIndexProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var boundaries = BoundarySeconds;
        var duration = DurationSeconds;
        if (boundaries is null || boundaries.Count == 0 || duration <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        var brush = TickBrush ?? new SolidColorBrush(Color.FromArgb(150, 120, 200, 255));
        var pen = new Pen(brush, 1.5);
        var activeBrush = new SolidColorBrush(Color.FromArgb(70, 120, 200, 255));
        var width = Bounds.Width;
        var height = Bounds.Height;

        for (var i = 0; i < boundaries.Count; i++)
        {
            var start = boundaries[i];
            if (start < 0 || start > duration)
            {
                continue;
            }

            var x = start / duration * width;

            if (i == ActiveIndex)
            {
                var end = i + 1 < boundaries.Count ? Math.Min(boundaries[i + 1], duration) : duration;
                var xEnd = end / duration * width;
                context.FillRectangle(activeBrush, new Rect(x, 0, Math.Max(1, xEnd - x), height));
            }

            context.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }
    }
}
