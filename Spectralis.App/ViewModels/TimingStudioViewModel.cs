using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.Core.Audio;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Metadata;

namespace Spectralis.App.ViewModels;

public sealed class TimingChip : ViewModelBase
{
    private bool _isTimed;
    private bool _isSelected;

    public TimingChip(string text, int globalIndex)
    {
        Text = text;
        GlobalIndex = globalIndex;
    }

    public string Text { get; }
    public int GlobalIndex { get; }

    public bool IsTimed
    {
        get => _isTimed;
        set => this.RaiseAndSetIfChanged(ref _isTimed, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public sealed class TimingChipLine
{
    public TimingChipLine(ObservableCollection<TimingChip> chips) => Chips = chips;
    public ObservableCollection<TimingChip> Chips { get; }
}

public sealed class TimedLineRow : ViewModelBase
{
    private string _timestampText = "--:--.--";
    private bool _isCurrent;
    private bool _isSelected;

    public TimedLineRow(string text) => Text = text;

    public string Text { get; }

    public string TimestampText
    {
        get => _timestampText;
        set => this.RaiseAndSetIfChanged(ref _timestampText, value);
    }

    public bool IsCurrent
    {
        get => _isCurrent;
        set => this.RaiseAndSetIfChanged(ref _isCurrent, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

public sealed class TimingStudioViewModel : ViewModelBase
{
    private readonly AudioEngine _engine;
    private readonly LyricsTimingSession _session = new();
    private string _plainText = string.Empty;
    private string _status = string.Empty;
    private string _positionText = "00:00.00";
    private bool _hasLines;
    private int _selectedIndex = -1;
    private bool _isWordMode;
    private int _chipCurrentIndex = -1;
    private readonly List<TimingChip> _allChips = [];

    public TimingStudioViewModel(AudioEngine engine)
    {
        _engine = engine;

        LoadLinesCommand = ReactiveCommand.Create(LoadLines);
        TapCommand = ReactiveCommand.Create(Tap);
        UndoCommand = ReactiveCommand.Create(Undo);
        ResetCommand = ReactiveCommand.Create(ResetStamps);
        ExportCommand = ReactiveCommand.Create(Export);
        EmbedInTagsCommand = ReactiveCommand.Create(EmbedInTags);
        PlayPauseCommand = ReactiveCommand.Create(PlayPause);
        NudgeCommand = ReactiveCommand.Create<string?>(NudgeFromParam);
        SeekToLineCommand = ReactiveCommand.Create(SeekToLine);
        CopyLrcCommand = ReactiveCommand.Create(CopyLrc);
    }

    public ObservableCollection<TimedLineRow> Rows { get; } = new();
    public ObservableCollection<TimingChipLine> ChipLines { get; } = new();

    public bool IsWordMode
    {
        get => _isWordMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _isWordMode, value);
            this.RaisePropertyChanged(nameof(IsLineMode));
            if (HasLines) RebuildChips();
        }
    }

    public bool IsLineMode
    {
        get => !_isWordMode;
        set
        {
            if (value) IsWordMode = false;
        }
    }

    public ReactiveCommand<Unit, Unit> LoadLinesCommand { get; }
    public ReactiveCommand<Unit, Unit> TapCommand { get; }
    public ReactiveCommand<Unit, Unit> UndoCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetCommand { get; }
    public ReactiveCommand<Unit, Unit> ExportCommand { get; }
    public ReactiveCommand<Unit, Unit> EmbedInTagsCommand { get; }
    public ReactiveCommand<Unit, Unit> PlayPauseCommand { get; }
    public ReactiveCommand<string?, Unit> NudgeCommand { get; }
    public ReactiveCommand<Unit, Unit> SeekToLineCommand { get; }
    public ReactiveCommand<Unit, Unit> CopyLrcCommand { get; }

    /// <summary>Set by the view to handle clipboard writes from the ViewModel.</summary>
    public Func<string, Task>? ClipboardWriter { get; set; }

    /// <summary>Called from the view's position tick timer (80 ms).</summary>
    public void TickPosition()
    {
        var pos = _engine.GetPosition();
        var cs = (long)Math.Round(pos * 100, MidpointRounding.AwayFromZero);
        var m = cs / 6000;
        var s = (cs / 100) % 60;
        var c = cs % 100;
        PositionText = $"{m:D2}:{s:D2}.{c:D2}";
    }

    public void SelectRow(int index)
    {
        if (_selectedIndex >= 0 && _selectedIndex < Rows.Count)
            Rows[_selectedIndex].IsSelected = false;
        _selectedIndex = index;
        if (_selectedIndex >= 0 && _selectedIndex < Rows.Count)
            Rows[_selectedIndex].IsSelected = true;
    }

    public string PlainText
    {
        get => _plainText;
        set => this.RaiseAndSetIfChanged(ref _plainText, value);
    }

    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public string PositionText
    {
        get => _positionText;
        private set => this.RaiseAndSetIfChanged(ref _positionText, value);
    }

    public bool HasLines
    {
        get => _hasLines;
        private set => this.RaiseAndSetIfChanged(ref _hasLines, value);
    }

    private void LoadLines()
    {
        _session.LoadPlainText(PlainText);
        Rows.Clear();
        foreach (var line in _session.Lines)
        {
            Rows.Add(new TimedLineRow(line.Text));
        }

        HasLines = Rows.Count > 0;
        Status = HasLines
            ? $"{Rows.Count} lines loaded. Play the track and tap to stamp."
            : "Paste lyrics on the left first.";
        UpdateCursor();
        RebuildChips();
    }

    private void RebuildChips()
    {
        ChipLines.Clear();
        _allChips.Clear();
        _chipCurrentIndex = 0;

        foreach (var line in _session.Lines)
        {
            var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var lineChips = new ObservableCollection<TimingChip>();
            foreach (var word in words)
            {
                var chip = new TimingChip(word, _allChips.Count);
                chip.IsTimed = line.Timestamp.HasValue;
                _allChips.Add(chip);
                lineChips.Add(chip);
            }
            if (lineChips.Count > 0)
                ChipLines.Add(new TimingChipLine(lineChips));
        }

        if (_allChips.Count > 0 && _chipCurrentIndex < _allChips.Count)
            _allChips[_chipCurrentIndex].IsSelected = true;
    }

    private void Tap()
    {
        if (!_engine.IsLoaded)
        {
            Status = "Load a track in Now Playing first.";
            return;
        }

        if (_isWordMode)
        {
            TapChip();
            return;
        }

        var stamped = _session.Tap(_engine.GetPosition());
        if (stamped < 0)
        {
            Status = "All lines are stamped. Export when ready.";
            return;
        }

        Rows[stamped].TimestampText = LyricsTimingSession.FormatTimestamp(_session.Lines[stamped].Timestamp!.Value);
        UpdateCursor();
        if (_session.IsComplete)
        {
            Status = "All lines stamped. Export the .lrc sidecar.";
        }
    }

    private void TapChip()
    {
        if (_chipCurrentIndex < 0 || _chipCurrentIndex >= _allChips.Count)
        {
            Status = "All words are stamped. Export when ready.";
            return;
        }

        var pos = _engine.GetPosition();
        _allChips[_chipCurrentIndex].IsTimed = true;
        _allChips[_chipCurrentIndex].IsSelected = false;
        _chipCurrentIndex++;
        if (_chipCurrentIndex < _allChips.Count)
            _allChips[_chipCurrentIndex].IsSelected = true;
        else
            Status = "All words stamped.";
    }

    public void SelectChip(int globalIndex)
    {
        if (_chipCurrentIndex >= 0 && _chipCurrentIndex < _allChips.Count)
            _allChips[_chipCurrentIndex].IsSelected = false;
        _chipCurrentIndex = globalIndex;
        if (_chipCurrentIndex >= 0 && _chipCurrentIndex < _allChips.Count)
            _allChips[_chipCurrentIndex].IsSelected = true;
    }

    private void Undo()
    {
        if (_isWordMode)
        {
            if (_chipCurrentIndex > 0)
            {
                if (_chipCurrentIndex < _allChips.Count)
                    _allChips[_chipCurrentIndex].IsSelected = false;
                _chipCurrentIndex--;
                _allChips[_chipCurrentIndex].IsTimed = false;
                _allChips[_chipCurrentIndex].IsSelected = true;
            }
            return;
        }

        if (_session.UndoLastTap())
        {
            Rows[_session.CurrentIndex].TimestampText = "--:--.--";
            UpdateCursor();
            Status = string.Empty;
        }
    }

    private void ResetStamps()
    {
        _session.Reset();
        foreach (var row in Rows)
        {
            row.TimestampText = "--:--.--";
        }

        foreach (var chip in _allChips)
        {
            chip.IsTimed = false;
            chip.IsSelected = false;
        }
        _chipCurrentIndex = 0;
        if (_allChips.Count > 0)
            _allChips[0].IsSelected = true;

        UpdateCursor();
        Status = string.Empty;
    }

    private void Export()
    {
        var track = _engine.CurrentTrack;
        if (track is null)
        {
            Status = "Load the matching track in Now Playing before exporting.";
            return;
        }

        try
        {
            var path = _session.SaveSidecar(track.SourcePath, track.Title, track.Artist);
            Status = $"Saved {Path.GetFileName(path)} next to the audio file.";
        }
        catch (Exception ex)
        {
            Status = $"Export failed: {ex.Message}";
        }
    }

    private void EmbedInTags()
    {
        var track = _engine.CurrentTrack;
        if (track is null || string.IsNullOrEmpty(track.SourcePath))
        {
            Status = "Load the matching track in Now Playing first.";
            return;
        }

        try
        {
            var lrc = _session.ExportLrc(track.Title, track.Artist);
            TagEditorService.WriteLyrics(track.SourcePath, lrc);
            Status = $"LRC embedded in {Path.GetFileName(track.SourcePath)} (Lyrics tag).";
        }
        catch (Exception ex)
        {
            Status = $"Embed failed: {ex.Message}";
        }
    }

    private void PlayPause()
    {
        if (!_engine.IsLoaded)
        {
            Status = "Load a track in Now Playing first.";
            return;
        }

        _engine.Toggle();
    }

    private void NudgeFromParam(string? param)
    {
        if (double.TryParse(param, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var delta))
            Nudge(delta);
    }

    private void Nudge(double deltaSec)
    {
        if (_selectedIndex < 0 || _selectedIndex >= _session.Lines.Count)
        {
            Status = "Select a line first (click a row).";
            return;
        }

        var line = _session.Lines[_selectedIndex];
        if (line.Timestamp is null)
        {
            Status = "Selected line has no timestamp to nudge.";
            return;
        }

        var newStamp = Math.Max(0, line.Timestamp.Value + deltaSec);
        _session.AdjustTimestamp(_selectedIndex, newStamp);
        Rows[_selectedIndex].TimestampText = LyricsTimingSession.FormatTimestamp(newStamp);
        Status = $"Line {_selectedIndex + 1} nudged to {LyricsTimingSession.FormatTimestamp(newStamp)}.";
    }

    private void SeekToLine()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _session.Lines.Count) return;
        var line = _session.Lines[_selectedIndex];
        if (line.Timestamp is null) return;
        _engine.Seek((float)line.Timestamp.Value);
    }

    private void CopyLrc()
    {
        if (ClipboardWriter is null)
        {
            Status = "Clipboard not available.";
            return;
        }

        var track = _engine.CurrentTrack;
        var lrc = _session.ExportLrc(track?.Title, track?.Artist);
        _ = ClipboardWriter(lrc);
        Status = "LRC copied to clipboard.";
    }

    private void UpdateCursor()
    {
        for (var i = 0; i < Rows.Count; i++)
        {
            Rows[i].IsCurrent = i == _session.CurrentIndex;
        }
    }
}
