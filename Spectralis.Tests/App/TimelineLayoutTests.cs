using Spectralis.App.Controls;
using Spectralis.Core.Formats;
using Xunit;

namespace Spectralis.Tests.App;

public class TimelineLayoutTests
{
    private static TimelineLayout Layout(double pxPerSecond = 50) => new() { PixelsPerSecond = pxPerSecond };

    private static ReactiveTimelineDocument Doc() => new()
    {
        Sections =
        [
            new ReactiveSection { Id = "a", Label = "A", Start = 0, End = 10 },
            new ReactiveSection { Id = "b", Label = "B", Start = 10, End = 20 },
        ],
        Timeline =
        [
            new ReactiveTimelineEvent { Time = 2, Target = "bg", Action = "fade", Duration = 4 },   // 0: lane 0, long
            new ReactiveTimelineEvent { Time = 5, Target = "logo", Action = "pop" },                // 1: lane 1, instant
        ],
    };

    private static readonly string[] Targets = ["bg", "logo"];

    [Fact]
    public void Time_and_x_round_trip_through_the_gutter()
    {
        var l = Layout(50);

        Assert.Equal(TimelineLayout.GutterWidth, l.TimeToX(0));
        Assert.Equal(TimelineLayout.GutterWidth + 100, l.TimeToX(2));
        Assert.Equal(2, l.XToTime(l.TimeToX(2)), 9);
        Assert.Equal(0, l.XToTime(5)); // inside the gutter clamps to zero
    }

    [Fact]
    public void Zoom_is_clamped()
    {
        var l = Layout();
        l.PixelsPerSecond = 0.01;
        Assert.Equal(TimelineLayout.MinZoom, l.PixelsPerSecond);
        l.PixelsPerSecond = 99999;
        Assert.Equal(TimelineLayout.MaxZoom, l.PixelsPerSecond);
    }

    [Fact]
    public void Zooming_keeps_the_time_under_the_pointer_fixed()
    {
        var l = Layout(40);
        var anchorX = 300.0;
        var scroll = 120.0;
        var timeBefore = l.XToTime(anchorX + scroll);

        var newScroll = l.ZoomAround(anchorX, scroll, 160);

        Assert.Equal(timeBefore, l.XToTime(anchorX + newScroll), 6);
        Assert.Equal(160, l.PixelsPerSecond);
    }

    [Theory]
    [InlineData(4, 30)]      // zoomed far out: 30s spacing keeps labels apart
    [InlineData(40, 2)]
    [InlineData(100, 1)]
    [InlineData(600, 0.25)]
    public void Tick_step_keeps_labels_a_readable_distance_apart(double pxPerSecond, double expectedStep)
    {
        var l = Layout(pxPerSecond);
        var step = l.TickStepSeconds();

        Assert.Equal(expectedStep, step);
        Assert.True(step * pxPerSecond >= 70);
    }

    [Fact]
    public void Lane_rows_are_computed_from_y()
    {
        Assert.Equal(-1, TimelineLayout.LaneAt(5));
        Assert.Equal(-1, TimelineLayout.LaneAt(TimelineLayout.LanesTop - 1));
        Assert.Equal(0, TimelineLayout.LaneAt(TimelineLayout.LaneTop(0) + 1));
        Assert.Equal(2, TimelineLayout.LaneAt(TimelineLayout.LaneTop(2) + 10));
    }

    [Fact]
    public void Hit_testing_finds_events_and_their_resize_edge()
    {
        var l = Layout(50);
        var doc = Doc();
        var laneY = TimelineLayout.LaneTop(0) + 10;

        var body = l.HitTest(l.TimeToX(3), laneY, doc, Targets);
        var end = l.HitTest(l.TimeToX(6) - 2, laneY, doc, Targets);

        Assert.Equal(new TimelineHit(TimelineHitKind.Event, 0, 0), body);
        Assert.Equal(new TimelineHit(TimelineHitKind.EventEnd, 0, 0), end);
    }

    [Fact]
    public void Instant_events_are_grabbable_but_not_resizable()
    {
        var l = Layout(50);
        var hit = l.HitTest(l.TimeToX(5) + 3, TimelineLayout.LaneTop(1) + 10, Doc(), Targets);

        Assert.Equal(new TimelineHit(TimelineHitKind.Event, 1, 1), hit);
    }

    [Fact]
    public void An_event_only_answers_in_its_own_lane()
    {
        var l = Layout(50);

        var wrongLane = l.HitTest(l.TimeToX(3), TimelineLayout.LaneTop(1) + 10, Doc(), Targets);

        Assert.Equal(TimelineHitKind.Lane, wrongLane.Kind);
        Assert.Equal(1, wrongLane.Lane);
    }

    [Fact]
    public void Empty_space_below_the_lanes_still_reports_a_lane_to_drop_into()
    {
        var l = Layout(50);
        var hit = l.HitTest(l.TimeToX(30), TimelineLayout.LaneTop(5) + 4, Doc(), Targets);

        Assert.Equal(new TimelineHit(TimelineHitKind.Lane, -1, 5), hit);
    }

    [Fact]
    public void Section_band_hits_split_into_body_and_edges()
    {
        var l = Layout(50);
        var y = TimelineLayout.SectionBandTop + 8;

        Assert.Equal(new TimelineHit(TimelineHitKind.Section, 0), l.HitTest(l.TimeToX(5), y, Doc(), Targets));
        Assert.Equal(new TimelineHit(TimelineHitKind.SectionStart, 0), l.HitTest(l.TimeToX(0) + 2, y, Doc(), Targets));
        Assert.Equal(TimelineHitKind.None, l.HitTest(l.TimeToX(40), y, Doc(), Targets).Kind);
    }

    [Fact]
    public void Where_two_sections_meet_the_boundary_hit_picks_one_consistently()
    {
        var l = Layout(50);
        var hit = l.HitTest(l.TimeToX(10), TimelineLayout.SectionBandTop + 8, Doc(), Targets);

        // The later section is checked first, so its start handle wins the shared boundary.
        Assert.Equal(new TimelineHit(TimelineHitKind.SectionStart, 1), hit);
    }

    [Fact]
    public void The_ruler_is_its_own_hit_area_for_seeking()
    {
        var hit = Layout().HitTest(200, 8, Doc(), Targets);

        Assert.Equal(TimelineHitKind.Ruler, hit.Kind);
    }

    [Fact]
    public void Narrow_events_get_a_minimum_grab_width()
    {
        var l = Layout(4);
        var thin = new ReactiveTimelineEvent { Time = 1, Target = "bg", Duration = 0.1 };

        Assert.Equal(TimelineLayout.MinEventWidth, l.EventWidth(thin));
    }

    [Fact]
    public void Content_size_grows_with_duration_and_lane_count()
    {
        var l = Layout(50);

        Assert.True(l.ContentWidth(120) > l.ContentWidth(60));
        Assert.True(l.ContentHeight(4) > l.ContentHeight(1));
        Assert.True(l.ContentHeight(0) >= l.ContentHeight(1)); // always room for one lane
    }
}
