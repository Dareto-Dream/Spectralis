using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Services;

namespace Spectralis.App.ViewModels;

public sealed class RandomizerToolsViewModel : ViewModelBase
{
    private string _coinResult = string.Empty;
    private bool _isFlipping;
    private bool _isSpinning;
    private string _wheelResult = string.Empty;
    private string _notepadText = string.Empty;
    private string _newWheelName = string.Empty;

    public RandomizerToolsViewModel()
    {
        WheelEntries = [];
        foreach (var text in new[] { "Option 1", "Option 2", "Option 3" })
            WheelEntries.Add(new WheelEntry(text));
        _notepadText = string.Join('\n', WheelEntries.Select(e => e.Text));

        FlipCoinCommand = ReactiveCommand.Create(RequestFlip,
            this.WhenAnyValue(x => x.IsFlipping, f => !f));

        SpinCommand = ReactiveCommand.Create(RequestSpin,
            this.WhenAnyValue(x => x.IsSpinning, s => !s));

        SaveWheelCommand = ReactiveCommand.Create(SaveWheel,
            this.WhenAnyValue(x => x.NewWheelName, x => !string.IsNullOrWhiteSpace(x)));

        RefreshSavedWheels();
    }

    public ObservableCollection<WheelEntry> WheelEntries { get; }
    public ObservableCollection<SavedWheel> SavedWheels { get; } = [];

    /// <summary>Raw multiline entry editor. One wheel entry per non-blank line.</summary>
    public string NotepadText
    {
        get => _notepadText;
        set
        {
            this.RaiseAndSetIfChanged(ref _notepadText, value);
            SyncEntriesFromNotepad();
        }
    }

    public string NewWheelName
    {
        get => _newWheelName;
        set => this.RaiseAndSetIfChanged(ref _newWheelName, value);
    }

    public string CoinResult
    {
        get => _coinResult;
        set => this.RaiseAndSetIfChanged(ref _coinResult, value);
    }

    public bool IsFlipping
    {
        get => _isFlipping;
        set => this.RaiseAndSetIfChanged(ref _isFlipping, value);
    }

    public bool IsSpinning
    {
        get => _isSpinning;
        set => this.RaiseAndSetIfChanged(ref _isSpinning, value);
    }

    public string WheelResult
    {
        get => _wheelResult;
        set
        {
            this.RaiseAndSetIfChanged(ref _wheelResult, value);
            this.RaisePropertyChanged(nameof(HasWheelResult));
        }
    }

    public bool HasWheelResult => !string.IsNullOrEmpty(_wheelResult);

    public ReactiveCommand<Unit, Unit> FlipCoinCommand { get; }
    public ReactiveCommand<Unit, Unit> SpinCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveWheelCommand { get; }

    public event Action? SpinRequested;
    public event Action? FlipRequested;

    private void RequestFlip()
    {
        IsFlipping = true;
        FlipRequested?.Invoke();
    }

    public void FinishFlip()
    {
        CoinResult = Random.Shared.Next(2) == 0 ? "Heads" : "Tails";
        IsFlipping = false;
    }

    private void RequestSpin()
    {
        if (WheelEntries.Count == 0) return;
        IsSpinning = true;
        WheelResult = string.Empty;
        SpinRequested?.Invoke();
    }

    public void FinishSpin(string result)
    {
        WheelResult = result;
        IsSpinning = false;
    }

    /// <summary>
    /// Reparses <see cref="NotepadText"/> into <see cref="WheelEntries"/>, one entry per non-blank
    /// line. Entries whose text is unchanged keep their existing color/font/weight settings
    /// (matched by text, not position, so reordering or editing other lines doesn't reset them).
    /// </summary>
    private void SyncEntriesFromNotepad()
    {
        var lines = (_notepadText ?? string.Empty)
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var pool = new Dictionary<string, Queue<WheelEntry>>(StringComparer.Ordinal);
        foreach (var entry in WheelEntries)
        {
            if (!pool.TryGetValue(entry.Text, out var queue))
                pool[entry.Text] = queue = new Queue<WheelEntry>();
            queue.Enqueue(entry);
        }

        var next = new List<WheelEntry>(lines.Count);
        foreach (var line in lines)
        {
            if (pool.TryGetValue(line, out var queue) && queue.Count > 0)
                next.Add(queue.Dequeue());
            else
                next.Add(new WheelEntry(line));
        }

        if (next.SequenceEqual(WheelEntries)) return;

        WheelEntries.Clear();
        foreach (var entry in next)
            WheelEntries.Add(entry);
    }

    private void RefreshSavedWheels()
    {
        SavedWheels.Clear();
        foreach (var wheel in SavedWheelStore.LoadAll())
            SavedWheels.Add(wheel);
    }

    private void SaveWheel()
    {
        var name = NewWheelName.Trim();
        if (string.IsNullOrEmpty(name) || WheelEntries.Count == 0) return;

        SavedWheelStore.Save(name, WheelEntries);
        NewWheelName = string.Empty;
        RefreshSavedWheels();
    }

    public void LoadWheel(SavedWheel wheel)
    {
        if (wheel is null) return;

        WheelEntries.Clear();
        foreach (var entry in wheel.Entries)
            WheelEntries.Add(entry.Clone());

        // Set the backing field directly (not the property) so the notepad text reflects the
        // loaded entries without re-parsing and discarding their color/font/weight settings.
        _notepadText = string.Join('\n', WheelEntries.Select(e => e.Text));
        this.RaisePropertyChanged(nameof(NotepadText));
    }

    public void DeleteWheel(SavedWheel wheel)
    {
        if (wheel is null) return;
        SavedWheelStore.Delete(wheel.Name);
        RefreshSavedWheels();
    }
}
