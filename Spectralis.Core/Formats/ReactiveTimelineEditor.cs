using System.Text.Json;

namespace Spectralis.Core.Formats;

/// <summary>
/// The editing model behind the timeline editor: add, move, resize and delete events and sections on a
/// <see cref="ReactiveTimelineDocument"/> with snapping and undo/redo, while keeping the document valid
/// at every step (sorted, finite, inside the track, sections never overlapping) so it can always be saved.
///
/// Events and sections are addressed by index into the sorted lists. Operations that can reorder
/// (<see cref="MoveEvent"/>, <see cref="AddEvent"/>…) return the new index, and undo/redo restores indices
/// exactly as they were.
/// </summary>
public sealed class ReactiveTimelineEditor
{
    public const double MinSectionLength = 0.1;
    private const int MaxUndo = 100;

    private static readonly JsonSerializerOptions SnapshotOptions = new() { MaxDepth = 16 };

    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private bool _inGesture;

    public ReactiveTimelineEditor(ReactiveTimelineDocument? document, double durationSeconds)
    {
        Duration = double.IsFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : 0;
        // Work on a private copy so cancelling an edit session can't leak into the loaded document.
        Document = document is null ? NewDocument() : Clone(document);
        Document.Format = ReactiveFormat.FormatName;
        Document.FormatVersion = ReactiveFormat.FormatVersion;
        Normalize();
    }

    public ReactiveTimelineDocument Document { get; private set; }

    /// <summary>Track length in seconds; nothing can be placed past it.</summary>
    public double Duration { get; }

    public bool SnapEnabled { get; set; } = true;

    /// <summary>Grid spacing in seconds used when <see cref="SnapEnabled"/> is on.</summary>
    public double SnapSeconds { get; set; } = 0.25;

    /// <summary>Beat positions to snap to as well as the grid (from beat detection), in seconds.</summary>
    public IReadOnlyList<double> BeatTimes { get; set; } = [];

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when there are edits since construction or the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty { get; private set; }

    public event Action? Changed;

    /// <summary>Lane names in order of first appearance (one lane per event target).</summary>
    public IReadOnlyList<string> Targets =>
        Document.Timeline.Select(e => e.Target).Where(t => t.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    // ── snapping ──────────────────────────────────────────────────────────────

    /// <summary>The nearest snap point (grid line or beat) to <paramref name="time"/>, clamped to the track.</summary>
    public double Snap(double time)
    {
        var t = ClampTime(time);
        if (!SnapEnabled || SnapSeconds <= 0)
        {
            return t;
        }

        var best = Math.Round(t / SnapSeconds) * SnapSeconds;
        foreach (var beat in BeatTimes)
        {
            if (Math.Abs(beat - t) < Math.Abs(best - t))
            {
                best = beat;
            }
        }
        return ClampTime(best);
    }

    /// <summary>Beat positions for a constant tempo: the first beat at <paramref name="firstBeatSeconds"/>, then every 60/bpm.</summary>
    public static IReadOnlyList<double> BeatsFromGrid(double bpm, double firstBeatSeconds, double durationSeconds)
    {
        if (!double.IsFinite(bpm) || bpm <= 0 || !double.IsFinite(durationSeconds) || durationSeconds <= 0)
        {
            return [];
        }

        var step = 60.0 / bpm;
        var first = Math.Max(0, double.IsFinite(firstBeatSeconds) ? firstBeatSeconds : 0);
        var beats = new List<double>();
        for (var t = first; t <= durationSeconds && beats.Count < 20_000; t += step)
        {
            beats.Add(t);
        }
        return beats;
    }

    private double ClampTime(double time) =>
        !double.IsFinite(time) ? 0 : Math.Clamp(time, 0, Duration > 0 ? Duration : double.MaxValue);

    // ── events ────────────────────────────────────────────────────────────────

    public int AddEvent(double time, string target, string action, double duration = 0, string easing = "linear")
    {
        Record();
        var start = Snap(time);
        var evt = new ReactiveTimelineEvent
        {
            Time = start,
            Target = target.Trim(),
            Action = action.Trim(),
            Duration = ClampLength(start, duration),
            Easing = string.IsNullOrWhiteSpace(easing) ? "linear" : easing.Trim(),
        };
        return Commit(() => InsertEvent(evt));
    }

    public int MoveEvent(int index, double newTime)
    {
        if (!HasEvent(index)) return -1;
        Record();
        var evt = Document.Timeline[index];
        Document.Timeline.RemoveAt(index);
        evt.Time = Snap(newTime);
        evt.Duration = ClampLength(evt.Time, evt.Duration);
        return Commit(() => InsertEvent(evt));
    }

    /// <summary>Changes how long the event runs, keeping its start. The end stays inside the track.</summary>
    public void ResizeEvent(int index, double newDuration)
    {
        if (!HasEvent(index)) return;
        Record();
        var evt = Document.Timeline[index];
        var end = Snap(evt.Time + Math.Max(0, newDuration));
        evt.Duration = Math.Max(0, end - evt.Time);
        Commit(() => index);
    }

    public void RemoveEvent(int index)
    {
        if (!HasEvent(index)) return;
        Record();
        Document.Timeline.RemoveAt(index);
        Commit(() => -1);
    }

    /// <summary>Copies an event <paramref name="offsetSeconds"/> later (params included). Returns the copy's index.</summary>
    public int DuplicateEvent(int index, double offsetSeconds)
    {
        if (!HasEvent(index) || Document.Timeline.Count >= ReactiveFormat.MaxTimelineEvents) return -1;
        Record();
        var copy = CloneEvent(Document.Timeline[index]);
        copy.Time = Snap(copy.Time + offsetSeconds);
        copy.Duration = ClampLength(copy.Time, copy.Duration);
        return Commit(() => InsertEvent(copy));
    }

    public void SetEventText(int index, string? target = null, string? action = null, string? easing = null)
    {
        if (!HasEvent(index)) return;
        Record();
        var evt = Document.Timeline[index];
        if (target is not null) evt.Target = target.Trim();
        if (action is not null) evt.Action = action.Trim();
        if (easing is not null) evt.Easing = string.IsNullOrWhiteSpace(easing) ? "linear" : easing.Trim();
        Commit(() => index);
    }

    /// <summary>Sets one parameter. Returns false when the event is missing or already has the maximum number of params.</summary>
    public bool SetParam(int index, string key, object? value)
    {
        if (!HasEvent(index) || string.IsNullOrWhiteSpace(key)) return false;
        var evt = Document.Timeline[index];
        if (!evt.Params.ContainsKey(key) && evt.Params.Count >= ReactiveFormat.MaxEventParams) return false;

        Record();
        evt.Params[key.Trim()] = value;
        Commit(() => index);
        return true;
    }

    public void RemoveParam(int index, string key)
    {
        if (!HasEvent(index) || !Document.Timeline[index].Params.ContainsKey(key)) return;
        Record();
        Document.Timeline[index].Params.Remove(key);
        Commit(() => index);
    }

    private bool HasEvent(int index) => index >= 0 && index < Document.Timeline.Count;

    /// <summary>Inserts keeping the list sorted by time; equal times keep insertion order. Returns the index.</summary>
    private int InsertEvent(ReactiveTimelineEvent evt)
    {
        var list = Document.Timeline;
        var at = list.Count;
        while (at > 0 && list[at - 1].Time > evt.Time) at--;
        list.Insert(at, evt);
        return at;
    }

    private double ClampLength(double start, double duration)
    {
        var length = double.IsFinite(duration) ? Math.Max(0, duration) : 0;
        return Duration > 0 ? Math.Min(length, Math.Max(0, Duration - start)) : length;
    }

    // ── sections ──────────────────────────────────────────────────────────────

    /// <summary>Adds a section; returns its index, or −1 if it would overlap another or be too short.</summary>
    public int AddSection(double start, double end, string label, string mood = "")
    {
        var s = Snap(start);
        var e = Snap(end);
        if (e - s < MinSectionLength || Overlaps(s, e, ignore: -1)) return -1;
        if (Document.Sections.Count >= ReactiveFormat.MaxSections) return -1;

        Record();
        var section = new ReactiveSection
        {
            Id = UniqueSectionId(label),
            Label = label.Trim(),
            Start = s,
            End = e,
            Mood = mood.Trim(),
        };
        return Commit(() => InsertSection(section));
    }

    /// <summary>Moves a section as a block; it stops against its neighbours instead of passing through them.</summary>
    public int MoveSection(int index, double newStart)
    {
        if (!HasSection(index)) return -1;
        var section = Document.Sections[index];
        var length = section.End - section.Start;
        var (floor, ceiling) = Neighbours(index);
        var start = Math.Clamp(Snap(newStart), floor, Math.Max(floor, ceiling - length));
        if (start == section.Start) return index;

        Record();
        Document.Sections.RemoveAt(index);
        section.Start = start;
        section.End = start + length;
        return Commit(() => InsertSection(section));
    }

    /// <summary>Drags one edge of a section, clamped to neighbours and the minimum length.</summary>
    public void ResizeSection(int index, bool startEdge, double time)
    {
        if (!HasSection(index)) return;
        var section = Document.Sections[index];
        var (floor, ceiling) = Neighbours(index);
        var t = Snap(time);

        var start = section.Start;
        var end = section.End;
        if (startEdge) start = Math.Clamp(t, floor, end - MinSectionLength);
        else end = Math.Clamp(t, start + MinSectionLength, ceiling);
        if (start == section.Start && end == section.End) return;

        Record();
        section.Start = start;
        section.End = end;
        Commit(() => index);
    }

    /// <summary>Splits a section in two at <paramref name="time"/>. Returns the index of the second half, or −1.</summary>
    public int SplitSection(int index, double time)
    {
        if (!HasSection(index) || Document.Sections.Count >= ReactiveFormat.MaxSections) return -1;
        var section = Document.Sections[index];
        var at = Snap(time);
        if (at - section.Start < MinSectionLength || section.End - at < MinSectionLength) return -1;

        Record();
        var second = new ReactiveSection
        {
            Id = UniqueSectionId(section.Label),
            Label = section.Label,
            Start = at,
            End = section.End,
            Mood = section.Mood,
        };
        section.End = at;
        return Commit(() => InsertSection(second));
    }

    public void RemoveSection(int index)
    {
        if (!HasSection(index)) return;
        Record();
        Document.Sections.RemoveAt(index);
        Commit(() => -1);
    }

    public void SetSectionText(int index, string? label = null, string? mood = null)
    {
        if (!HasSection(index)) return;
        Record();
        var section = Document.Sections[index];
        if (label is not null) section.Label = label.Trim();
        if (mood is not null) section.Mood = mood.Trim();
        Commit(() => index);
    }

    private bool HasSection(int index) => index >= 0 && index < Document.Sections.Count;

    private int InsertSection(ReactiveSection section)
    {
        var list = Document.Sections;
        var at = list.Count;
        while (at > 0 && list[at - 1].Start > section.Start) at--;
        list.Insert(at, section);
        return at;
    }

    private bool Overlaps(double start, double end, int ignore) =>
        Document.Sections.Where((_, i) => i != ignore).Any(s => start < s.End && end > s.Start);

    /// <summary>How far a section may stretch: the previous section's end and the next one's start.</summary>
    private (double Floor, double Ceiling) Neighbours(int index)
    {
        var floor = index > 0 ? Document.Sections[index - 1].End : 0;
        var ceiling = index < Document.Sections.Count - 1
            ? Document.Sections[index + 1].Start
            : (Duration > 0 ? Duration : double.MaxValue);
        return (floor, ceiling);
    }

    private string UniqueSectionId(string label)
    {
        var slug = new string(label.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (slug.Length == 0) slug = "section";

        var id = slug;
        for (var n = 2; Document.Sections.Any(s => s.Id == id); n++) id = $"{slug}-{n}";
        return id;
    }

    // ── undo / redo ───────────────────────────────────────────────────────────

    public bool Undo() => Swap(_undo, _redo);

    public bool Redo() => Swap(_redo, _undo);

    private bool Swap(List<string> from, List<string> to)
    {
        if (from.Count == 0) return false;
        to.Add(Snapshot());
        Document = Restore(from[^1]);
        from.RemoveAt(from.Count - 1);
        IsDirty = true;
        Changed?.Invoke();
        return true;
    }

    private void Record()
    {
        if (_inGesture) return; // a drag is one undo step, recorded when it began
        _undo.Add(Snapshot());
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
        _redo.Clear();
    }

    private int Commit(Func<int> apply)
    {
        var result = apply();
        IsDirty = true;
        Changed?.Invoke();
        return result;
    }

    public void MarkSaved() => IsDirty = false;

    // ── gestures ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a drag. Every edit until <see cref="EndGesture"/> shares one undo entry, so dragging an event
    /// across the track is a single Ctrl+Z rather than one step per pointer move.
    /// </summary>
    public void BeginGesture()
    {
        if (_inGesture) return;
        Record();
        _inGesture = true;
    }

    /// <summary>Ends a drag. If nothing actually changed (a click, or a drag back to the start) no undo step is kept.</summary>
    public void EndGesture()
    {
        if (!_inGesture) return;
        _inGesture = false;
        if (_undo.Count > 0 && _undo[^1] == Snapshot())
        {
            _undo.RemoveAt(_undo.Count - 1);
        }
    }

    // ── validation + saving ───────────────────────────────────────────────────

    /// <summary>Things worth fixing before saving. An empty list means the timeline is good to go.</summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        for (var i = 0; i < Document.Timeline.Count; i++)
        {
            var e = Document.Timeline[i];
            if (e.Target.Length == 0) problems.Add($"Event at {e.Time:0.00}s has no target.");
            if (e.Action.Length == 0) problems.Add($"Event at {e.Time:0.00}s has no action.");
        }
        if (!Document.IsValid()) problems.Add("The timeline breaks the file format's limits.");
        return problems;
    }

    /// <summary>Writes the sidecar next to the audio file and marks the editor clean.</summary>
    public string Save(string audioPath)
    {
        var problems = Validate();
        if (problems.Count > 0) throw new InvalidDataException(problems[0]);

        var path = ReactiveTimelineLoader.SaveSidecar(audioPath, Document);
        MarkSaved();
        return path;
    }

    // ── internals ─────────────────────────────────────────────────────────────

    private static ReactiveTimelineDocument NewDocument() => new()
    {
        Format = ReactiveFormat.FormatName,
        FormatVersion = ReactiveFormat.FormatVersion,
    };

    private void Normalize()
    {
        Document.Timeline = Document.Timeline.OrderBy(e => e.Time).ToList(); // stable sort
        foreach (var e in Document.Timeline)
        {
            e.Time = ClampTime(e.Time);
            e.Duration = ClampLength(e.Time, e.Duration);
        }
        Document.Sections = Document.Sections.OrderBy(s => s.Start).ToList();
    }

    private string Snapshot() => JsonSerializer.Serialize(Document, SnapshotOptions);

    private static ReactiveTimelineDocument Restore(string json) =>
        JsonSerializer.Deserialize<ReactiveTimelineDocument>(json, SnapshotOptions) ?? NewDocument();

    private static ReactiveTimelineDocument Clone(ReactiveTimelineDocument document) =>
        Restore(JsonSerializer.Serialize(document, SnapshotOptions));

    private static ReactiveTimelineEvent CloneEvent(ReactiveTimelineEvent evt) =>
        JsonSerializer.Deserialize<ReactiveTimelineEvent>(JsonSerializer.Serialize(evt, SnapshotOptions), SnapshotOptions)!;
}
