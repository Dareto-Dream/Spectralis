using Spectralis.Core.Formats;

namespace Spectralis.App.Controls;

public enum TimelineHitKind
{
    None,
    Ruler,
    /// <summary>Empty space in a lane (or below the last lane): a place to add an event.</summary>
    Lane,
    Event,
    /// <summary>The right-hand edge of an event that has a length: drag to resize it.</summary>
    EventEnd,
    Section,
    SectionStart,
    SectionEnd,
}

/// <summary>What is under the pointer. <see cref="Index"/> is an event or section index; <see cref="Lane"/> the lane row.</summary>
public readonly record struct TimelineHit(TimelineHitKind Kind, int Index = -1, int Lane = -1);

/// <summary>
/// Geometry for the timeline editor: time ↔ pixel conversion, lane rows and hit-testing. Pure maths with no
/// UI types, so it's testable and the drawing control stays a thin shell around it.
///
/// Top to bottom: ruler, section band, then one lane per event target. A fixed gutter on the left holds the
/// lane names; time zero starts at <see cref="GutterWidth"/>.
/// </summary>
public sealed class TimelineLayout
{
    public const double GutterWidth = 96;
    public const double RulerHeight = 24;
    public const double SectionBandHeight = 30;
    public const double LaneHeight = 34;
    public const double MinEventWidth = 10;
    public const double EdgeGrabPixels = 6;
    public const double MinZoom = 4;
    public const double MaxZoom = 600;

    private double _pixelsPerSecond = 40;

    public double PixelsPerSecond
    {
        get => _pixelsPerSecond;
        set => _pixelsPerSecond = Math.Clamp(value, MinZoom, MaxZoom);
    }

    public double TimeToX(double seconds) => GutterWidth + (seconds * PixelsPerSecond);

    public double XToTime(double x) => Math.Max(0, (x - GutterWidth) / PixelsPerSecond);

    public double ContentWidth(double durationSeconds) => GutterWidth + (Math.Max(1, durationSeconds) * PixelsPerSecond) + 40;

    public double ContentHeight(int laneCount) =>
        RulerHeight + SectionBandHeight + (Math.Max(1, laneCount) * LaneHeight) + LaneHeight; // + a spare lane to drop into

    public static double SectionBandTop => RulerHeight;
    public static double LanesTop => RulerHeight + SectionBandHeight;

    public static double LaneTop(int lane) => LanesTop + (lane * LaneHeight);

    /// <summary>Lane row under a y coordinate, or −1 above the lanes.</summary>
    public static int LaneAt(double y) => y < LanesTop ? -1 : (int)((y - LanesTop) / LaneHeight);

    /// <summary>Pixel width of an event: its duration, but never so thin you can't grab it.</summary>
    public double EventWidth(ReactiveTimelineEvent evt) => Math.Max(MinEventWidth, evt.Duration * PixelsPerSecond);

    /// <summary>A "nice" ruler spacing in seconds so labels stay about 70–140 px apart at any zoom.</summary>
    public double TickStepSeconds()
    {
        double[] steps = [0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300];
        foreach (var step in steps)
        {
            if (step * PixelsPerSecond >= 70) return step;
        }
        return steps[^1];
    }

    /// <summary>Zooms around a point so the time under <paramref name="anchorX"/> stays put. Returns the new scroll offset.</summary>
    public double ZoomAround(double anchorX, double scrollOffset, double newPixelsPerSecond)
    {
        var anchorTime = XToTime(anchorX + scrollOffset);
        PixelsPerSecond = newPixelsPerSecond;
        return Math.Max(0, TimeToX(anchorTime) - anchorX);
    }

    /// <summary>
    /// Finds what's at (<paramref name="x"/>, <paramref name="y"/>), in content coordinates. Events win over
    /// sections win over empty lane space; the later of two overlapping events wins, as it's drawn on top.
    /// </summary>
    public TimelineHit HitTest(double x, double y, ReactiveTimelineDocument document, IReadOnlyList<string> targets)
    {
        if (y < RulerHeight) return new TimelineHit(TimelineHitKind.Ruler);

        if (y < LanesTop)
        {
            for (var i = document.Sections.Count - 1; i >= 0; i--)
            {
                var s = document.Sections[i];
                var left = TimeToX(s.Start);
                var right = TimeToX(s.End);
                if (x < left - EdgeGrabPixels || x > right + EdgeGrabPixels) continue;

                if (Math.Abs(x - left) <= EdgeGrabPixels) return new TimelineHit(TimelineHitKind.SectionStart, i);
                if (Math.Abs(x - right) <= EdgeGrabPixels) return new TimelineHit(TimelineHitKind.SectionEnd, i);
                return new TimelineHit(TimelineHitKind.Section, i);
            }
            return new TimelineHit(TimelineHitKind.None);
        }

        var lane = LaneAt(y);
        for (var i = document.Timeline.Count - 1; i >= 0; i--)
        {
            var evt = document.Timeline[i];
            var eventLane = IndexOf(targets, evt.Target);
            if (eventLane != lane) continue;

            var left = TimeToX(evt.Time);
            var width = EventWidth(evt);
            if (x < left - EdgeGrabPixels || x > left + width + EdgeGrabPixels) continue;

            var resizable = evt.Duration > 0;
            if (resizable && Math.Abs(x - (left + width)) <= EdgeGrabPixels && x > left + EdgeGrabPixels)
            {
                return new TimelineHit(TimelineHitKind.EventEnd, i, lane);
            }
            if (x >= left - EdgeGrabPixels && x <= left + width + EdgeGrabPixels) return new TimelineHit(TimelineHitKind.Event, i, lane);
        }

        return new TimelineHit(TimelineHitKind.Lane, Lane: lane);
    }

    private static int IndexOf(IReadOnlyList<string> targets, string target)
    {
        for (var i = 0; i < targets.Count; i++)
        {
            if (string.Equals(targets[i], target, StringComparison.Ordinal)) return i;
        }
        return -1;
    }
}
