using System.Globalization;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Controls;
using Spectralis.App.Services;
using Spectralis.Core.Audio;
using Spectralis.Core.Formats;

namespace Spectralis.App.ViewModels;

/// <summary>
/// Editor for a track's reactive timeline (.spectralis-reactive.json): sections and timed events on one
/// lane per target, edited against the playing track. Sits next to the lyric Timing Studio.
/// All editing rules (snapping, undo, non-overlapping sections) live in <see cref="ReactiveTimelineEditor"/>;
/// this class owns selection, the inspector fields, zoom and the link to playback.
/// </summary>
public sealed class TimelineEditorViewModel : ViewModelBase
{
    private const string DefaultTarget = "visualizer";
    private const double DefaultSectionSeconds = 8;

    private static readonly double[] SnapSteps = [0.0625, 0.125, 0.25, 0.5, 1];

    private readonly AudioEngine _engine;
    private readonly NowPlayingViewModel? _nowPlaying;

    private ReactiveTimelineEditor? _editor;
    private string? _loadedPath;
    private string _trackTitle = "";
    private string _status = "Load a track in Now Playing, then come back here.";
    private double _position;
    private int _selectedEvent = -1;
    private int _selectedSection = -1;
    private bool _snapEnabled = true;
    private bool _useBeatSnap = true;
    private SelectionOption<double> _selectedSnap;
    private double _viewportWidth;
    private double _lastBpm = double.NaN;
    private double _lastBeatOffset = double.NaN;
    private string _lastAction = "pulse";

    // inspector fields (bound as text; applied on demand)
    private string _eventTarget = "";
    private string _eventAction = "";
    private string _eventEasing = "linear";
    private string _eventTimeText = "";
    private string _eventDurationText = "";
    private string _eventParamsText = "";
    private string _sectionLabel = "";
    private string _sectionMood = "";

    public TimelineEditorViewModel(AudioEngine engine, NowPlayingViewModel? nowPlaying = null)
    {
        _engine = engine;
        _nowPlaying = nowPlaying;
        SnapOptions = SnapSteps.Select(s => new SelectionOption<double>(SnapLabel(s), s)).ToList();
        _selectedSnap = SnapOptions[2];

        AddEventCommand = ReactiveCommand.Create(AddEventAtPlayhead);
        AddSectionCommand = ReactiveCommand.Create(AddSectionAtPlayhead);
        SplitSectionCommand = ReactiveCommand.Create(SplitSectionAtPlayhead);
        DuplicateCommand = ReactiveCommand.Create(DuplicateSelected);
        DeleteCommand = ReactiveCommand.Create(DeleteSelected);
        UndoCommand = ReactiveCommand.Create(() => { _editor?.Undo(); AfterEdit(); });
        RedoCommand = ReactiveCommand.Create(() => { _editor?.Redo(); AfterEdit(); });
        SaveCommand = ReactiveCommand.Create(Save);
        ReloadCommand = ReactiveCommand.Create(ReloadFromDisk);
        PlayPauseCommand = ReactiveCommand.Create(() => _engine.Toggle());
        ApplyInspectorCommand = ReactiveCommand.Create(ApplyInspector);
        ZoomInCommand = ReactiveCommand.Create(() => { Zoom *= 1.4; });
        ZoomOutCommand = ReactiveCommand.Create(() => { Zoom /= 1.4; });
        FitCommand = ReactiveCommand.Create(FitToWindow);
    }

    public TimelineLayout Layout { get; } = new();

    public ReactiveTimelineEditor? Editor
    {
        get => _editor;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editor, value);
            this.RaisePropertyChanged(nameof(HasTrack));
            this.RaisePropertyChanged(nameof(DurationSeconds));
        }
    }

    public bool HasTrack => _editor is not null;
    public double DurationSeconds => _editor?.Duration ?? 0;

    /// <summary>The canvas redraws on this: any edit, selection change, zoom or playhead move.</summary>
    public event Action? Invalidated;

    public string TrackTitle
    {
        get => _trackTitle;
        private set => this.RaiseAndSetIfChanged(ref _trackTitle, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public double PositionSeconds => _position;

    public bool IsPlaying => _engine.IsPlaying;

    public string PositionText => FormatTime(_position);

    public string DurationText => FormatTime(DurationSeconds);

    public bool IsDirty => _editor?.IsDirty ?? false;
    public bool CanUndo => _editor?.CanUndo ?? false;
    public bool CanRedo => _editor?.CanRedo ?? false;

    public string SaveButtonText => IsDirty ? "Save •" : "Save";

    // ── snapping + zoom ──────────────────────────────────────────────────────

    public IReadOnlyList<SelectionOption<double>> SnapOptions { get; }

    public SelectionOption<double> SelectedSnap
    {
        get => _selectedSnap;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSnap, value);
            ApplySnapSettings();
        }
    }

    public bool SnapEnabled
    {
        get => _snapEnabled;
        set
        {
            this.RaiseAndSetIfChanged(ref _snapEnabled, value);
            ApplySnapSettings();
        }
    }

    public bool UseBeatSnap
    {
        get => _useBeatSnap;
        set
        {
            this.RaiseAndSetIfChanged(ref _useBeatSnap, value);
            RefreshBeatTimes(force: true);
        }
    }

    public bool HasBeatGrid => (_nowPlaying?.BeatGridBpm ?? 0) > 0;

    public double Zoom
    {
        get => Layout.PixelsPerSecond;
        set
        {
            Layout.PixelsPerSecond = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(ContentWidth));
            Invalidated?.Invoke();
        }
    }

    public double ContentWidth => Layout.ContentWidth(DurationSeconds);

    public double ContentHeight => Layout.ContentHeight(_editor?.Targets.Count ?? 0);

    /// <summary>Width of the scroll area, reported by the view so "fit" knows how much room there is.</summary>
    public double ViewportWidth
    {
        get => _viewportWidth;
        set => this.RaiseAndSetIfChanged(ref _viewportWidth, value);
    }

    private void FitToWindow()
    {
        if (DurationSeconds <= 0 || _viewportWidth <= TimelineLayout.GutterWidth + 80) return;
        Zoom = (_viewportWidth - TimelineLayout.GutterWidth - 60) / DurationSeconds;
    }

    private void ApplySnapSettings()
    {
        if (_editor is null) return;
        _editor.SnapEnabled = _snapEnabled;
        _editor.SnapSeconds = _selectedSnap.Value;
        Invalidated?.Invoke();
    }

    private void RefreshBeatTimes(bool force = false)
    {
        var bpm = _nowPlaying?.BeatGridBpm ?? 0;
        var offset = _nowPlaying?.BeatGridOffsetSeconds ?? 0;
        if (!force && bpm == _lastBpm && offset == _lastBeatOffset) return;

        _lastBpm = bpm;
        _lastBeatOffset = offset;
        this.RaisePropertyChanged(nameof(HasBeatGrid));
        if (_editor is null) return;
        _editor.BeatTimes = _useBeatSnap ? ReactiveTimelineEditor.BeatsFromGrid(bpm, offset, _editor.Duration) : [];
        Invalidated?.Invoke();
    }

    // ── selection + inspector ────────────────────────────────────────────────

    public int SelectedEventIndex => _selectedEvent;
    public int SelectedSectionIndex => _selectedSection;
    public bool HasEventSelection => _editor is not null && _selectedEvent >= 0 && _selectedEvent < _editor.Document.Timeline.Count;
    public bool HasSectionSelection => _editor is not null && _selectedSection >= 0 && _selectedSection < _editor.Document.Sections.Count;
    public bool HasSelection => HasEventSelection || HasSectionSelection;

    public void SelectEvent(int index)
    {
        _selectedEvent = index;
        _selectedSection = -1;
        LoadInspector();
        RaiseSelection();
    }

    public void SelectSection(int index)
    {
        _selectedSection = index;
        _selectedEvent = -1;
        LoadInspector();
        RaiseSelection();
    }

    public void ClearSelection()
    {
        _selectedEvent = -1;
        _selectedSection = -1;
        LoadInspector();
        RaiseSelection();
    }

    private void RaiseSelection()
    {
        this.RaisePropertyChanged(nameof(SelectedEventIndex));
        this.RaisePropertyChanged(nameof(SelectedSectionIndex));
        this.RaisePropertyChanged(nameof(HasEventSelection));
        this.RaisePropertyChanged(nameof(HasSectionSelection));
        this.RaisePropertyChanged(nameof(HasSelection));
        Invalidated?.Invoke();
    }

    public string EventTarget { get => _eventTarget; set => this.RaiseAndSetIfChanged(ref _eventTarget, value); }
    public string EventAction { get => _eventAction; set => this.RaiseAndSetIfChanged(ref _eventAction, value); }
    public string EventEasing { get => _eventEasing; set => this.RaiseAndSetIfChanged(ref _eventEasing, value); }
    public string EventTimeText { get => _eventTimeText; set => this.RaiseAndSetIfChanged(ref _eventTimeText, value); }
    public string EventDurationText { get => _eventDurationText; set => this.RaiseAndSetIfChanged(ref _eventDurationText, value); }
    public string EventParamsText { get => _eventParamsText; set => this.RaiseAndSetIfChanged(ref _eventParamsText, value); }
    public string SectionLabel { get => _sectionLabel; set => this.RaiseAndSetIfChanged(ref _sectionLabel, value); }
    public string SectionMood { get => _sectionMood; set => this.RaiseAndSetIfChanged(ref _sectionMood, value); }

    private void LoadInspector()
    {
        if (HasEventSelection)
        {
            var evt = _editor!.Document.Timeline[_selectedEvent];
            EventTarget = evt.Target;
            EventAction = evt.Action;
            EventEasing = evt.Easing;
            EventTimeText = evt.Time.ToString("0.###", CultureInfo.InvariantCulture);
            EventDurationText = evt.Duration.ToString("0.###", CultureInfo.InvariantCulture);
            EventParamsText = ReactiveParamText.Format(evt.Params);
        }
        else if (HasSectionSelection)
        {
            var section = _editor!.Document.Sections[_selectedSection];
            SectionLabel = section.Label;
            SectionMood = section.Mood;
        }
    }

    private void ApplyInspector()
    {
        if (_editor is null) return;

        if (HasEventSelection)
        {
            var index = _selectedEvent;
            _editor.BeginGesture(); // one undo step for the whole inspector apply
            _editor.SetEventText(index, EventTarget, EventAction, EventEasing);
            if (TryParseSeconds(EventDurationText, out var duration))
            {
                _editor.ResizeEvent(index, duration);
            }
            if (TryParseSeconds(EventTimeText, out var time))
            {
                index = _editor.MoveEvent(index, time);
            }

            // Replace params wholesale with what the text says.
            foreach (var key in _editor.Document.Timeline[index].Params.Keys.ToList()) _editor.RemoveParam(index, key);
            foreach (var (key, value) in ReactiveParamText.Parse(EventParamsText)) _editor.SetParam(index, key, value);
            _editor.EndGesture();

            if (!string.IsNullOrWhiteSpace(EventAction)) _lastAction = EventAction.Trim();
            _selectedEvent = index;
            Status = "Event updated.";
        }
        else if (HasSectionSelection)
        {
            _editor.SetSectionText(_selectedSection, SectionLabel, SectionMood);
            Status = "Section updated.";
        }

        AfterEdit();
        LoadInspector();
    }

    private static bool TryParseSeconds(string text, out double seconds) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && double.IsFinite(seconds) && seconds >= 0;

    // ── commands ─────────────────────────────────────────────────────────────

    public ReactiveCommand<Unit, Unit> AddEventCommand { get; }
    public ReactiveCommand<Unit, Unit> AddSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> SplitSectionCommand { get; }
    public ReactiveCommand<Unit, Unit> DuplicateCommand { get; }
    public ReactiveCommand<Unit, Unit> DeleteCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> RedoCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCommand { get; }
    public ReactiveCommand<Unit, Unit> ReloadCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<Unit, Unit> ApplyInspectorCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomInCommand { get; }
    public ReactiveCommand<Unit, Unit> ZoomOutCommand { get; }
    public ReactiveCommand<Unit, Unit> FitCommand { get; }

    private void AddEventAtPlayhead()
    {
        if (_editor is null) return;
        var target = HasEventSelection ? _editor.Document.Timeline[_selectedEvent].Target : FirstLaneOrDefault();
        SelectEvent(_editor.AddEvent(_position, target, _lastAction));
        AfterEdit();
        Status = $"Added an event on \"{target}\" at {FormatTime(_editor.Document.Timeline[_selectedEvent].Time)}.";
    }

    /// <summary>Adds an event in a given lane at a time — used by double-clicking empty lane space.</summary>
    public void AddEventAt(double time, int lane)
    {
        if (_editor is null) return;
        var targets = _editor.Targets;
        var target = lane >= 0 && lane < targets.Count ? targets[lane] : (lane >= targets.Count && targets.Count > 0 ? NewLaneName(targets.Count) : FirstLaneOrDefault());
        SelectEvent(_editor.AddEvent(time, target, _lastAction));
        AfterEdit();
    }

    private string FirstLaneOrDefault() => _editor is { Targets.Count: > 0 } e ? e.Targets[0] : DefaultTarget;

    private static string NewLaneName(int existing) => $"lane-{existing + 1}";

    private void AddSectionAtPlayhead()
    {
        if (_editor is null) return;
        var start = _position;
        var index = _editor.AddSection(start, start + DefaultSectionSeconds, "Section");
        if (index < 0)
        {
            // Sections can't overlap: fill whatever gap there is before the next section instead.
            var next = _editor.Document.Sections.FirstOrDefault(s => s.Start > start);
            var end = next?.Start ?? Math.Min(_editor.Duration, start + DefaultSectionSeconds);
            index = _editor.AddSection(start, end, "Section");
        }

        if (index < 0)
        {
            Status = "There's no free space for a section at the playhead. Move it or shrink a neighbour.";
            return;
        }

        SelectSection(index);
        AfterEdit();
        Status = "Added a section. Rename it in the inspector.";
    }

    private void SplitSectionAtPlayhead()
    {
        if (_editor is null) return;
        var index = _editor.Document.Sections.FindIndex(s => _position > s.Start && _position < s.End);
        if (index < 0 || _editor.SplitSection(index, _position) < 0)
        {
            Status = "Put the playhead inside a section (not at its edge) to split it.";
            return;
        }
        AfterEdit();
        Status = "Split the section at the playhead.";
    }

    private void DuplicateSelected()
    {
        if (_editor is null || !HasEventSelection) return;
        var offset = Math.Max(_selectedSnap.Value, 1.0);
        var copy = _editor.DuplicateEvent(_selectedEvent, offset);
        if (copy >= 0) SelectEvent(copy);
        AfterEdit();
    }

    private void DeleteSelected()
    {
        if (_editor is null) return;
        if (HasEventSelection) _editor.RemoveEvent(_selectedEvent);
        else if (HasSectionSelection) _editor.RemoveSection(_selectedSection);
        else return;

        ClearSelection();
        AfterEdit();
        Status = "Deleted. Ctrl+Z brings it back.";
    }

    private void Save()
    {
        if (_editor is null || _loadedPath is null)
        {
            Status = "Nothing to save yet.";
            return;
        }

        try
        {
            var path = _editor.Save(_loadedPath);
            Status = $"Saved {Path.GetFileName(path)}.";
        }
        catch (Exception ex)
        {
            Status = $"Couldn't save: {ex.Message}";
        }
        AfterEdit();
    }

    private void ReloadFromDisk()
    {
        if (_loadedPath is not null) LoadTrack(_loadedPath);
    }

    // ── canvas drag API ──────────────────────────────────────────────────────

    public void BeginDrag() => _editor?.BeginGesture();

    public void EndDrag()
    {
        _editor?.EndGesture();
        AfterEdit();
        LoadInspector();
    }

    public void DragEvent(double newTime)
    {
        if (_editor is null || !HasEventSelection) return;
        _selectedEvent = _editor.MoveEvent(_selectedEvent, newTime);
        AfterEdit(refreshInspector: false);
    }

    public void DragEventEnd(double endTime)
    {
        if (_editor is null || !HasEventSelection) return;
        var evt = _editor.Document.Timeline[_selectedEvent];
        _editor.ResizeEvent(_selectedEvent, endTime - evt.Time);
        AfterEdit(refreshInspector: false);
    }

    public void DragSection(double newStart)
    {
        if (_editor is null || !HasSectionSelection) return;
        _selectedSection = _editor.MoveSection(_selectedSection, newStart);
        AfterEdit(refreshInspector: false);
    }

    public void DragSectionEdge(bool startEdge, double time)
    {
        if (_editor is null || !HasSectionSelection) return;
        _editor.ResizeSection(_selectedSection, startEdge, time);
        AfterEdit(refreshInspector: false);
    }

    public void SeekTo(double seconds)
    {
        if (!_engine.IsLoaded) return;
        _engine.Seek((float)Math.Clamp(seconds, 0, DurationSeconds));
        TickPosition();
    }

    // ── track + playback sync ────────────────────────────────────────────────

    /// <summary>Called from the view's timer: moves the playhead and follows the loaded track.</summary>
    public void TickPosition()
    {
        var path = _engine.CurrentTrack?.SourcePath;
        if (path is not null && !string.Equals(path, _loadedPath, StringComparison.OrdinalIgnoreCase))
        {
            if (IsDirty)
            {
                Status = "Another track is playing. Save or reload to switch this editor to it.";
            }
            else
            {
                LoadTrack(path);
            }
        }
        else if (path is null && _loadedPath is not null && !IsDirty)
        {
            Unload();
        }

        RefreshBeatTimes();

        var position = _engine.IsLoaded ? _engine.GetPosition() : 0;
        if (Math.Abs(position - _position) > 0.0005)
        {
            _position = position;
            this.RaisePropertyChanged(nameof(PositionSeconds));
            this.RaisePropertyChanged(nameof(PositionText));
            Invalidated?.Invoke();
        }
    }

    private void LoadTrack(string path)
    {
        var duration = _engine.GetLength();
        if (duration <= 0) return;

        var document = ReactiveTimelineLoader.LoadSidecar(path);
        var editor = new ReactiveTimelineEditor(document, duration)
        {
            SnapEnabled = _snapEnabled,
            SnapSeconds = _selectedSnap.Value,
        };
        editor.Changed += OnEditorChanged;

        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _loadedPath = path;
        TrackTitle = _engine.CurrentTrack is { } t && !string.IsNullOrWhiteSpace(t.Title) ? t.Title : Path.GetFileNameWithoutExtension(path);
        Editor = editor;
        _lastBpm = double.NaN;
        RefreshBeatTimes(force: true);
        ClearSelection();
        AfterEdit();

        Status = document is null
            ? "No timeline for this track yet. Double-click a lane to add an event, or use Add event at the playhead."
            : $"Loaded {document.Timeline.Count} events and {document.Sections.Count} sections.";
    }

    private void Unload()
    {
        if (_editor is not null) _editor.Changed -= OnEditorChanged;
        _loadedPath = null;
        TrackTitle = "";
        Editor = null;
        ClearSelection();
        AfterEdit();
        Status = "Load a track in Now Playing, then come back here.";
    }

    private void OnEditorChanged() => AfterEdit(refreshInspector: false);

    private void AfterEdit(bool refreshInspector = true)
    {
        // Indices can go stale after undo/redo or a delete; never keep pointing past the end.
        if (_editor is null)
        {
            _selectedEvent = _selectedSection = -1;
        }
        else
        {
            if (_selectedEvent >= _editor.Document.Timeline.Count) _selectedEvent = -1;
            if (_selectedSection >= _editor.Document.Sections.Count) _selectedSection = -1;
        }

        this.RaisePropertyChanged(nameof(IsDirty));
        this.RaisePropertyChanged(nameof(CanUndo));
        this.RaisePropertyChanged(nameof(CanRedo));
        this.RaisePropertyChanged(nameof(SaveButtonText));
        this.RaisePropertyChanged(nameof(ContentWidth));
        this.RaisePropertyChanged(nameof(ContentHeight));
        this.RaisePropertyChanged(nameof(HasEventSelection));
        this.RaisePropertyChanged(nameof(HasSectionSelection));
        this.RaisePropertyChanged(nameof(HasSelection));
        if (refreshInspector) LoadInspector();
        Invalidated?.Invoke();
    }

    // ── formatting ───────────────────────────────────────────────────────────

    public static string FormatTime(double seconds)
    {
        var cs = (long)Math.Round(Math.Max(0, seconds) * 100, MidpointRounding.AwayFromZero);
        return $"{cs / 6000:D2}:{(cs / 100) % 60:D2}.{cs % 100:D2}";
    }

    private static string SnapLabel(double seconds) =>
        seconds < 0.1 ? "1/16 s" : seconds < 0.2 ? "1/8 s" : seconds < 0.4 ? "1/4 s" : seconds < 0.9 ? "1/2 s" : "1 s";
}
