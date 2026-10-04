using Spectralis.Core.Formats;
using Xunit;

namespace Spectralis.Tests.Core;

public class ReactiveTimelineEditorTests
{
    private static ReactiveTimelineEditor Editor(double duration = 60, double snap = 0.25)
    {
        var editor = new ReactiveTimelineEditor(null, duration) { SnapSeconds = snap };
        return editor;
    }

    private static double[] Times(ReactiveTimelineEditor e) => e.Document.Timeline.Select(x => x.Time).ToArray();

    // ── events ────────────────────────────────────────────────────────────────

    [Fact]
    public void Events_stay_sorted_by_time_and_report_their_index()
    {
        var e = Editor();

        e.AddEvent(10, "bg", "fade");
        var early = e.AddEvent(2, "bg", "pulse");
        var mid = e.AddEvent(5, "logo", "spin");

        Assert.Equal([2, 5, 10], Times(e));
        Assert.Equal(0, early);
        Assert.Equal(1, mid);
    }

    [Fact]
    public void Times_snap_to_the_grid_and_clamp_to_the_track()
    {
        var e = Editor(duration: 30, snap: 0.5);

        e.AddEvent(3.37, "bg", "x");
        e.AddEvent(-4, "bg", "x");
        e.AddEvent(999, "bg", "x");

        Assert.Equal([0, 3.5, 30], Times(e));
    }

    [Fact]
    public void Snapping_prefers_a_nearer_beat_over_the_grid_and_can_be_switched_off()
    {
        var e = Editor(snap: 1.0);
        e.BeatTimes = [3.1, 7.9];

        Assert.Equal(3.1, e.Snap(3.2), 6);   // beat is closer than the grid line at 3.0
        Assert.Equal(5.0, e.Snap(5.2), 6);   // no beat nearby, grid wins

        e.SnapEnabled = false;
        Assert.Equal(5.2, e.Snap(5.2), 6);
    }

    [Fact]
    public void Moving_an_event_resorts_it_and_returns_the_new_index()
    {
        var e = Editor(snap: 0);
        e.AddEvent(1, "a", "one");
        e.AddEvent(2, "a", "two");
        e.AddEvent(3, "a", "three");

        var moved = e.MoveEvent(0, 2.5);

        Assert.Equal(1, moved);
        Assert.Equal(["two", "one", "three"], e.Document.Timeline.Select(x => x.Action));
    }

    [Fact]
    public void An_event_cannot_run_past_the_end_of_the_track()
    {
        var e = Editor(duration: 10, snap: 0);
        var i = e.AddEvent(8, "bg", "fade", duration: 5);

        Assert.Equal(2, e.Document.Timeline[i].Duration, 6);

        e.ResizeEvent(i, 50);
        Assert.Equal(2, e.Document.Timeline[i].Duration, 6);

        e.MoveEvent(i, 9.5);
        Assert.Equal(0.5, e.Document.Timeline[0].Duration, 6);
    }

    [Fact]
    public void Duplicate_copies_params_independently()
    {
        var e = Editor(snap: 0);
        var i = e.AddEvent(1, "bg", "tint");
        e.SetParam(i, "color", "#ff0000");

        var copy = e.DuplicateEvent(i, 2);
        e.SetParam(copy, "color", "#00ff00");

        Assert.Equal(2, e.Document.Timeline.Count);
        Assert.Equal(3, e.Document.Timeline[copy].Time, 6);
        Assert.Equal("#ff0000", e.Document.Timeline[0].Params["color"]?.ToString());
        Assert.Equal("#00ff00", e.Document.Timeline[copy].Params["color"]?.ToString());
    }

    [Fact]
    public void Params_can_be_set_and_removed_and_are_capped()
    {
        var e = Editor(snap: 0);
        var i = e.AddEvent(1, "bg", "x");

        Assert.True(e.SetParam(i, "a", 1));
        e.RemoveParam(i, "a");
        Assert.Empty(e.Document.Timeline[i].Params);

        for (var n = 0; n < ReactiveFormat.MaxEventParams; n++) Assert.True(e.SetParam(i, $"p{n}", n));
        Assert.False(e.SetParam(i, "one-too-many", 1));
        Assert.True(e.SetParam(i, "p0", 99)); // replacing an existing key is still fine
        Assert.False(e.SetParam(i, " ", 1));
    }

    [Fact]
    public void Bad_indices_are_ignored_not_thrown_on()
    {
        var e = Editor();

        Assert.Equal(-1, e.MoveEvent(3, 1));
        Assert.Equal(-1, e.DuplicateEvent(-1, 1));
        e.RemoveEvent(7);
        e.ResizeEvent(2, 1);
        Assert.False(e.SetParam(0, "k", 1));
        Assert.False(e.CanUndo);
    }

    [Fact]
    public void Lanes_follow_event_targets_in_order_of_first_appearance()
    {
        var e = Editor(snap: 0);
        e.AddEvent(5, "logo", "a");
        e.AddEvent(1, "bg", "b");
        e.AddEvent(6, "bg", "c");

        Assert.Equal(["bg", "logo"], e.Targets);
    }

    // ── sections ──────────────────────────────────────────────────────────────

    [Fact]
    public void Sections_cannot_overlap_or_be_tiny()
    {
        var e = Editor(snap: 0);

        Assert.Equal(0, e.AddSection(0, 10, "Intro"));
        Assert.Equal(-1, e.AddSection(5, 15, "Clash"));
        Assert.Equal(-1, e.AddSection(20, 20.05, "Sliver"));
        Assert.Equal(1, e.AddSection(10, 20, "Verse"));
        Assert.Equal(["intro", "verse"], e.Document.Sections.Select(s => s.Id));
    }

    [Fact]
    public void Section_ids_stay_unique_for_repeated_labels()
    {
        var e = Editor(snap: 0);
        e.AddSection(0, 5, "Chorus");
        e.AddSection(10, 15, "Chorus");
        e.AddSection(20, 25, "Chorus");

        Assert.Equal(["chorus", "chorus-2", "chorus-3"], e.Document.Sections.Select(s => s.Id));
    }

    [Fact]
    public void Moving_a_section_stops_against_its_neighbours()
    {
        var e = Editor(snap: 0);
        e.AddSection(0, 10, "A");
        e.AddSection(12, 20, "B");   // 8s long
        e.AddSection(30, 40, "C");

        var index = e.MoveSection(1, 28); // would overlap C; B should stop right against it

        Assert.Equal(1, index);
        Assert.Equal(22, e.Document.Sections[1].Start, 6);
        Assert.Equal(30, e.Document.Sections[1].End, 6);

        e.MoveSection(1, -50); // and against A on the other side
        Assert.Equal(10, e.Document.Sections[1].Start, 6);
    }

    [Fact]
    public void Resizing_a_section_edge_respects_neighbours_and_minimum_length()
    {
        var e = Editor(snap: 0);
        e.AddSection(0, 10, "A");
        e.AddSection(15, 25, "B");

        e.ResizeSection(0, startEdge: false, time: 40);   // wants to swallow B
        Assert.Equal(15, e.Document.Sections[0].End, 6);

        e.ResizeSection(1, startEdge: true, time: 0);     // wants to swallow A
        Assert.Equal(15, e.Document.Sections[1].Start, 6);

        e.ResizeSection(0, startEdge: false, time: -3);   // can't shrink below the minimum
        Assert.Equal(ReactiveTimelineEditor.MinSectionLength, e.Document.Sections[0].End, 6);
    }

    [Fact]
    public void Splitting_a_section_makes_two_that_meet_exactly()
    {
        var e = Editor(snap: 0);
        e.AddSection(0, 20, "Verse", "calm");

        var second = e.SplitSection(0, 8);

        Assert.Equal(1, second);
        Assert.Equal((0, 8), (e.Document.Sections[0].Start, e.Document.Sections[0].End));
        Assert.Equal((8, 20), (e.Document.Sections[1].Start, e.Document.Sections[1].End));
        Assert.Equal("calm", e.Document.Sections[1].Mood);
        Assert.Equal(-1, e.SplitSection(0, 0.01)); // too close to an edge
    }

    // ── undo / redo ───────────────────────────────────────────────────────────

    [Fact]
    public void Undo_and_redo_walk_through_edits_and_restore_indices()
    {
        var e = Editor(snap: 0);
        e.AddEvent(1, "a", "one");
        e.AddEvent(2, "a", "two");
        e.RemoveEvent(0);

        Assert.Equal(["two"], e.Document.Timeline.Select(x => x.Action));

        Assert.True(e.Undo());
        Assert.Equal(["one", "two"], e.Document.Timeline.Select(x => x.Action));
        Assert.True(e.Undo());
        Assert.True(e.Undo());
        Assert.Empty(e.Document.Timeline);
        Assert.False(e.Undo());

        Assert.True(e.Redo());
        Assert.True(e.Redo());
        Assert.True(e.Redo());
        Assert.Equal(["two"], e.Document.Timeline.Select(x => x.Action));
        Assert.False(e.Redo());
    }

    [Fact]
    public void A_new_edit_after_undo_clears_the_redo_stack()
    {
        var e = Editor(snap: 0);
        e.AddEvent(1, "a", "one");
        e.Undo();
        Assert.True(e.CanRedo);

        e.AddEvent(2, "a", "other");

        Assert.False(e.CanRedo);
    }

    [Fact]
    public void Undo_depth_is_bounded()
    {
        var e = Editor(snap: 0);
        for (var n = 0; n < 150; n++) e.AddEvent(n % 50, "a", "x");

        var undone = 0;
        while (e.Undo()) undone++;

        Assert.Equal(100, undone);
    }

    [Fact]
    public void Params_survive_an_undo_round_trip()
    {
        var e = Editor(snap: 0);
        var i = e.AddEvent(1, "bg", "tint");
        e.SetParam(i, "amount", 0.5);
        e.SetParam(i, "enabled", true);
        e.RemoveEvent(i);

        e.Undo();

        var p = e.Document.Timeline[0].Params;
        Assert.Equal("0.5", p["amount"]?.ToString());
        Assert.Equal("True", p["enabled"]?.ToString());
    }

    [Fact]
    public void Changed_fires_for_edits_and_undo_and_dirty_tracks_saves()
    {
        var e = Editor(snap: 0);
        var fired = 0;
        e.Changed += () => fired++;
        Assert.False(e.IsDirty);

        e.AddEvent(1, "a", "x");
        e.Undo();

        Assert.Equal(2, fired);
        Assert.True(e.IsDirty);
        e.MarkSaved();
        Assert.False(e.IsDirty);
    }

    // ── gestures ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_drag_is_one_undo_step_however_many_moves_it_makes()
    {
        var e = Editor(snap: 0);
        var index = e.AddEvent(1, "bg", "fade");

        e.BeginGesture();
        for (var t = 2.0; t <= 20; t += 1)
        {
            index = e.MoveEvent(index, t);
        }
        e.EndGesture();

        Assert.Equal(20, Times(e)[0]);
        Assert.True(e.Undo());          // one step back puts it where the drag began...
        Assert.Equal(1, Times(e)[0]);
        Assert.True(e.Undo());          // ...and the next undo removes the event that was added
        Assert.Empty(e.Document.Timeline);
        Assert.False(e.Undo());
    }

    [Fact]
    public void A_gesture_that_changes_nothing_leaves_no_undo_step()
    {
        var e = Editor(snap: 0);
        var index = e.AddEvent(5, "bg", "fade");

        e.BeginGesture();
        index = e.MoveEvent(index, 9);
        e.MoveEvent(index, 5);          // dragged back to where it started
        e.EndGesture();

        Assert.Equal(5, Times(e)[0]);
        e.Undo();                       // goes straight to undoing the add, not a phantom drag
        Assert.Empty(e.Document.Timeline);
    }

    [Fact]
    public void Edits_outside_a_gesture_record_individually_again_afterwards()
    {
        var e = Editor(snap: 0);
        e.BeginGesture();
        e.AddEvent(1, "a", "one");
        e.EndGesture();

        e.AddEvent(2, "a", "two");
        e.AddEvent(3, "a", "three");

        var undone = 0;
        while (e.Undo()) undone++;
        Assert.Equal(3, undone);
    }

    // ── loading, validation, saving ───────────────────────────────────────────

    [Fact]
    public void Existing_documents_are_copied_and_normalised()
    {
        var source = new ReactiveTimelineDocument
        {
            Format = ReactiveFormat.FormatName,
            FormatVersion = ReactiveFormat.FormatVersion,
            Timeline =
            [
                new ReactiveTimelineEvent { Time = 9, Target = "a", Action = "late" },
                new ReactiveTimelineEvent { Time = 100, Target = "a", Action = "past-end", Duration = 5 },
                new ReactiveTimelineEvent { Time = 1, Target = "a", Action = "early" },
            ],
        };

        var e = new ReactiveTimelineEditor(source, 20);
        e.AddEvent(5, "a", "added");

        Assert.Equal(3, source.Timeline.Count);                  // the original is untouched
        Assert.Equal(["early", "added", "late", "past-end"], e.Document.Timeline.Select(x => x.Action));
        Assert.Equal(20, e.Document.Timeline[3].Time);          // clamped into the track
        Assert.Equal(0, e.Document.Timeline[3].Duration);
    }

    [Fact]
    public void Validate_flags_events_missing_a_target_or_action()
    {
        var e = Editor(snap: 0);
        e.AddEvent(1, "", "fade");
        e.AddEvent(2, "bg", "");

        var problems = e.Validate();

        Assert.Equal(2, problems.Count);
        Assert.Throws<InvalidDataException>(() => e.Save(Path.Combine(Path.GetTempPath(), "never.mp3")));
    }

    [Fact]
    public void Saving_writes_a_sidecar_the_loader_accepts_with_everything_intact()
    {
        var dir = Path.Combine(Path.GetTempPath(), "timeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var audio = Path.Combine(dir, "song.mp3");
        try
        {
            var e = Editor(duration: 120, snap: 0);
            e.AddSection(0, 30, "Intro", "calm");
            var i = e.AddEvent(12.5, "bg", "tint", duration: 3, easing: "easeInOut");
            e.SetParam(i, "color", "#112233");
            e.SetParam(i, "amount", 0.75);

            var path = e.Save(audio);

            Assert.Equal(ReactiveTimelineLoader.GetSidecarPath(audio), path);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.False(e.IsDirty);

            var loaded = ReactiveTimelineLoader.LoadSidecar(audio);
            Assert.NotNull(loaded);
            Assert.Equal("Intro", loaded!.Sections[0].Label);
            Assert.Equal("calm", loaded.Sections[0].Mood);
            var evt = Assert.Single(loaded.Timeline);
            Assert.Equal(12.5, evt.Time);
            Assert.Equal(3, evt.Duration);
            Assert.Equal("easeInOut", evt.Easing);
            Assert.Equal("#112233", evt.Params["color"]?.ToString());
            Assert.Equal("0.75", evt.Params["amount"]?.ToString());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Serialize_always_stamps_the_current_format()
    {
        var json = ReactiveTimelineLoader.Serialize(new ReactiveTimelineDocument());

        Assert.NotNull(ReactiveTimelineLoader.Parse(json));
    }
}
