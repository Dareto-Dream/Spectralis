using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;

namespace Spectralis.App.ViewModels;

public sealed class RandomizerToolsViewModel : ViewModelBase
{
    private string _coinResult = string.Empty;
    private bool _isFlipping;
    private bool _isSpinning;
    private string _wheelResult = string.Empty;
    private string _newEntryText = string.Empty;

    public RandomizerToolsViewModel()
    {
        WheelEntries = ["Option 1", "Option 2", "Option 3"];

        FlipCoinCommand = ReactiveCommand.Create(RequestFlip,
            this.WhenAnyValue(x => x.IsFlipping, f => !f));

        AddEntryCommand = ReactiveCommand.Create(TryAddEntry,
            this.WhenAnyValue(x => x.NewEntryText, t => !string.IsNullOrWhiteSpace(t)));

        SpinCommand = ReactiveCommand.Create(RequestSpin,
            this.WhenAnyValue(x => x.IsSpinning, s => !s));
    }

    public ObservableCollection<string> WheelEntries { get; }

    public string NewEntryText
    {
        get => _newEntryText;
        set => this.RaiseAndSetIfChanged(ref _newEntryText, value);
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
    public ReactiveCommand<Unit, Unit> AddEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> SpinCommand { get; }

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

    public void TryAddEntry()
    {
        var trimmed = NewEntryText.Trim();
        if (string.IsNullOrEmpty(trimmed)) return;
        WheelEntries.Add(trimmed);
        NewEntryText = string.Empty;
    }

    public void RemoveEntry(string entry) => WheelEntries.Remove(entry);
}
