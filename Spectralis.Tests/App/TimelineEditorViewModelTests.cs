using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Spectralis.Core.Formats;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class TimelineEditorViewModelTests : IDisposable
{
    private readonly FakeAudioDeviceEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timeline-vm-" + Guid.NewGuid().ToString("N"));
    private readonly TimelineEditorViewModel _vm;

    public TimelineEditorViewModelTests()
    {
        Directory.CreateDirectory(_dir);
        _engine = new AudioEngine(_devices);
        _vm = new TimelineEditorViewModel(_engine);
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    /// <summary>A 10-second wav in the temp folder, loaded into the engine.</summary>
    private string LoadTrack(string name = "song")
    {
        var generated = WavFixture.CreateSineWav(10.0, 8000, 2);
        var path = Path.Combine(_dir, name + ".wav");
        File.Move(generated, path, overwrite: true);
        _engine.Load(path);
        return path;
    }

    [Fact]
    public void Nothing_is_editable_until_a_track_is_loaded()
    {
        _vm.TickPosition();

        Assert.False(_vm.HasTrack);
        Assert.Null(_vm.Editor);
    }

    [Fact]
    public void The_playing_track_is_picked_up_with_no_sidecar_as_an_empty_timeline()
    {
        LoadTrack();

        _vm.TickPosition();

        Assert.True(_vm.HasTrack);
        Assert.InRange(_vm.DurationSeconds, 9.5, 10.5);
        Assert.Empty(_vm.Editor!.Document.Timeline);
        Assert.False(_vm.IsDirty);
    }

    [Fact]
    public void An_existing_sidecar_is_loaded()
    {
        var path = LoadTrack();
        var seed = new ReactiveTimelineEditor(null, 10);
        seed.AddEvent(2, "bg", "fade");
        seed.AddSection(0, 5, "Intro");
        seed.Save(path);

        _vm.TickPosition();

        Assert.Single(_vm.Editor!.Document.Timeline);
        Assert.Single(_vm.Editor.Document.Sections);
        Assert.Contains("Loaded 1 events", _vm.Status);
    }

    [Fact]
    public void Double_click_style_adds_select_the_new_event_and_mark_dirty()
    {
        LoadTrack();
        _vm.TickPosition();

        _vm.AddEventAt(3.0, lane: 0);

        Assert.True(_vm.HasEventSelection);
        Assert.Equal("visualizer", _vm.EventTarget);
        Assert.True(_vm.IsDirty);
        Assert.True(_vm.CanUndo);
        Assert.Equal("Save •", _vm.SaveButtonText);
    }

    [Fact]
    public void Adding_below_the_existing_lanes_creates_a_new_lane()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(1, 0);

        _vm.AddEventAt(2, lane: 4);

        Assert.Equal(["visualizer", "lane-2"], _vm.Editor!.Targets);
    }

    [Fact]
    public void A_drag_through_the_view_model_is_one_undo_step_and_follows_the_event()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(1, 0);
        _vm.Editor!.SnapEnabled = false;

        _vm.BeginDrag();
        for (var t = 2.0; t <= 8; t++) _vm.DragEvent(t);
        _vm.EndDrag();

        Assert.Equal(8, _vm.Editor.Document.Timeline[_vm.SelectedEventIndex].Time);
        _vm.UndoCommand.Execute().Subscribe();
        Assert.Equal(1, _vm.Editor.Document.Timeline[0].Time);
    }

    [Fact]
    public void Dragging_an_event_past_another_keeps_the_selection_on_the_dragged_one()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.Editor!.SnapEnabled = false;
        _vm.AddEventAt(1, 0);
        _vm.Editor.AddEvent(5, "visualizer", "other");
        _vm.SelectEvent(0);

        _vm.BeginDrag();
        _vm.DragEvent(7);
        _vm.EndDrag();

        Assert.Equal("pulse", _vm.Editor.Document.Timeline[_vm.SelectedEventIndex].Action);
        Assert.Equal(1, _vm.SelectedEventIndex);
    }

    [Fact]
    public void Delete_clears_the_selection_and_undo_brings_the_event_back()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(1, 0);

        _vm.DeleteCommand.Execute().Subscribe();

        Assert.False(_vm.HasSelection);
        Assert.Empty(_vm.Editor!.Document.Timeline);
        _vm.UndoCommand.Execute().Subscribe();
        Assert.Single(_vm.Editor.Document.Timeline);
    }

    [Fact]
    public void Undo_never_leaves_the_selection_pointing_past_the_end()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(1, 0);
        _vm.AddEventAt(2, 0);
        Assert.Equal(1, _vm.SelectedEventIndex);

        _vm.UndoCommand.Execute().Subscribe();

        Assert.False(_vm.HasEventSelection || _vm.SelectedEventIndex >= _vm.Editor!.Document.Timeline.Count);
    }

    [Fact]
    public void The_inspector_edits_time_length_and_typed_params_as_one_undo_step()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.Editor!.SnapEnabled = false;
        _vm.AddEventAt(1, 0);
        var before = _vm.Editor.Document.Timeline[0].Action;

        _vm.EventAction = "tint";
        _vm.EventTimeText = "4.5";
        _vm.EventDurationText = "2";
        _vm.EventParamsText = "amount=0.5\nenabled=true\ncolor=#ff8800";
        _vm.ApplyInspectorCommand.Execute().Subscribe();

        var evt = _vm.Editor.Document.Timeline[_vm.SelectedEventIndex];
        Assert.Equal("tint", evt.Action);
        Assert.Equal(4.5, evt.Time);
        Assert.Equal(2, evt.Duration);
        Assert.Equal(0.5, evt.Params["amount"]);
        Assert.Equal(true, evt.Params["enabled"]);

        _vm.UndoCommand.Execute().Subscribe();
        Assert.Equal(before, _vm.Editor.Document.Timeline[0].Action);
        Assert.Equal(1, _vm.Editor.Document.Timeline[0].Time);
        Assert.Empty(_vm.Editor.Document.Timeline[0].Params);
    }

    [Fact]
    public void Garbage_in_the_time_field_is_ignored_not_applied()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.Editor!.SnapEnabled = false;
        _vm.AddEventAt(3, 0);

        _vm.EventTimeText = "soon";
        _vm.EventDurationText = "-5";
        _vm.ApplyInspectorCommand.Execute().Subscribe();

        Assert.Equal(3, _vm.Editor.Document.Timeline[0].Time);
        Assert.Equal(0, _vm.Editor.Document.Timeline[0].Duration);
    }

    [Fact]
    public void Save_writes_the_sidecar_and_clears_the_dirty_flag()
    {
        var path = LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(2, 0);

        _vm.SaveCommand.Execute().Subscribe();

        Assert.False(_vm.IsDirty);
        Assert.Equal("Save", _vm.SaveButtonText);
        Assert.Single(ReactiveTimelineLoader.LoadSidecar(path)!.Timeline);
        Assert.StartsWith("Saved", _vm.Status);
    }

    [Fact]
    public void Saving_an_invalid_timeline_reports_why_instead_of_throwing()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(2, 0);
        _vm.EventAction = "";
        _vm.ApplyInspectorCommand.Execute().Subscribe();

        _vm.SaveCommand.Execute().Subscribe();

        Assert.StartsWith("Couldn't save", _vm.Status);
        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void A_different_track_does_not_replace_unsaved_edits()
    {
        var first = LoadTrack("first");
        _vm.TickPosition();
        _vm.AddEventAt(2, 0);

        LoadTrack("second");
        _vm.TickPosition();

        Assert.Single(_vm.Editor!.Document.Timeline);          // still the first track's edits
        Assert.Contains("Save or reload", _vm.Status);

        _vm.SaveCommand.Execute().Subscribe();                 // saves against the track being edited
        Assert.True(File.Exists(ReactiveTimelineLoader.GetSidecarPath(first)));
        _vm.TickPosition();                                    // now clean, so it switches over
        Assert.Empty(_vm.Editor!.Document.Timeline);
    }

    [Fact]
    public void Reload_discards_edits()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.AddEventAt(2, 0);

        _vm.ReloadCommand.Execute().Subscribe();

        Assert.Empty(_vm.Editor!.Document.Timeline);
        Assert.False(_vm.IsDirty);
        Assert.False(_vm.HasSelection);
    }

    [Fact]
    public void Section_commands_work_at_the_playhead_and_never_overlap()
    {
        LoadTrack();
        _vm.TickPosition();
        _vm.Editor!.SnapEnabled = false;

        _vm.AddSectionCommand.Execute().Subscribe();
        Assert.Single(_vm.Editor.Document.Sections);

        // The playhead is still at 0, inside the section just added: a second add must not stack on it.
        _vm.AddSectionCommand.Execute().Subscribe();
        Assert.Single(_vm.Editor.Document.Sections);
        Assert.Contains("no free space", _vm.Status);
    }

    [Fact]
    public void Zoom_is_clamped_and_fit_uses_the_viewport()
    {
        LoadTrack();
        _vm.TickPosition();

        _vm.Zoom = 100000;
        Assert.Equal(Spectralis.App.Controls.TimelineLayout.MaxZoom, _vm.Zoom);

        _vm.ViewportWidth = 1200;
        _vm.FitCommand.Execute().Subscribe();
        Assert.InRange(_vm.ContentWidth, 1000, 1300);
    }

    [Fact]
    public void Time_formatting_matches_the_timing_studio()
    {
        Assert.Equal("00:00.00", TimelineEditorViewModel.FormatTime(0));
        Assert.Equal("01:05.25", TimelineEditorViewModel.FormatTime(65.25));
        Assert.Equal("00:00.00", TimelineEditorViewModel.FormatTime(-3));
    }
}
