using System.Collections.ObjectModel;
using System.Reactive;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Audio.Dj;
using Spectralis.Core.Metadata;
using Spectralis.Core.Platform;

namespace Spectralis.App.ViewModels;

public sealed class DjViewModel : ViewModelBase, IDisposable
{
    private readonly AppSettings _settings;
    private readonly Action _beforePlay, _saveSettings;
    private readonly LibraryDatabase? _library;
    private readonly DjCueStore _cues;
    internal IDjSourceService Sources { get; }
    public Func<IEnumerable<DjLibraryTrack>>? QueueSource { get; set; }
    public Action? BrowseLibrary { get; set; }
    public Func<int, Task>? LoadNowPlaying { get; set; }
    public Action? ShowNowPlaying { get; set; }
    public string TempoDifference => DeckA.HasTrack && DeckB.HasTrack ? $"Tempo difference: {Math.Abs(DeckA.EffectiveBpm - DeckB.EffectiveBpm):0.0} BPM" : "Load tracks to compare";
    private bool _useForVisualization;
    public bool UseForVisualization { get => _useForVisualization; set => this.RaiseAndSetIfChanged(ref _useForVisualization, value); }
    private bool _outputStarted, _enabled;
    private int _activationVersion;
    private string _status = "Load a track on each deck to start a set.";
    private string _search = "";
    private string _browserSource = "Library";
    private int _activeDeckIndex;
    private bool[] _resumeDecks = [false, false];
    private double _unmutedVolume = 80;
    private AudioDeviceInfo? _master, _headphones;
    public DjMixerEngine Mixer { get; }
    public DjDeckViewModel DeckA { get; }
    public DjDeckViewModel DeckB { get; }
    public ObservableCollection<AudioDeviceInfo> OutputDevices { get; } = [];
    public ObservableCollection<AudioDeviceInfo> CueDevices { get; } = [];
    public ObservableCollection<DjLibraryTrack> LibraryTracks { get; } = [];
    public DjLibraryTrack? SelectedTrack { get; set; }
    public bool Enabled => _enabled;
    public bool AiEnabled => _settings.DjAiStemSeparationEnabled;
    public string[] BrowserSources { get; } = ["Library", "Queue"];
    public string BrowserSource { get => _browserSource; set { this.RaiseAndSetIfChanged(ref _browserSource, value); RefreshLibrary(); } }
    public int ActiveDeckIndex { get => _activeDeckIndex; set { this.RaiseAndSetIfChanged(ref _activeDeckIndex, value == 1 ? 1 : 0); DeckA.RefreshSelection(); DeckB.RefreshSelection(); } }
    public DjDeckViewModel ActiveDeck => ActiveDeckIndex == 0 ? DeckA : DeckB;
    public bool HasSession => Enabled && (DeckA.HasTrack || DeckB.HasTrack || DeckA.Busy || DeckB.Busy);
    public bool IsPlaying => DeckA.IsPlaying || DeckB.IsPlaying;
    public string PlayPauseLabel => IsPlaying ? "Pause mix" : "Play mix";
    public string SessionTitle => $"A: {DeckA.Title}  ·  B: {DeckB.Title}";
    public CancellationTokenSource? AiTaskCancellation { get; set; }
    public string Status { get => _status; set => this.RaiseAndSetIfChanged(ref _status, value); }
    public string Search { get => _search; set { this.RaiseAndSetIfChanged(ref _search, value); RefreshLibrary(); } }
    public double Crossfader { get => Mixer.Crossfader; set { Mixer.Crossfader = value; this.RaisePropertyChanged(); } }
    public double MasterVolume { get => Mixer.MasterVolume * 100; set { Mixer.MasterVolume = value / 100; this.RaisePropertyChanged(); } }
    public double CueVolume { get => Mixer.CueVolume * 100; set { Mixer.CueVolume = value / 100; this.RaisePropertyChanged(); } }
    public double MasterPeak => Mixer.MasterPeak * 100;
    public bool Clipping => Mixer.Clipping;
    public bool IsRecording => Mixer.IsRecording;
    public string RecordingLabel => IsRecording ? "Stop recording" : "Record mix…";
    public AudioDeviceInfo? MasterDevice
    {
        get => _master;
        set { if (value == _master) return; _master = value; _settings.DjMasterOutputDeviceId = value?.Id; _saveSettings(); RestartOutput(); this.RaisePropertyChanged(); }
    }
    public AudioDeviceInfo? HeadphoneDevice
    {
        get => _headphones;
        set { if (value == _headphones) return; _headphones = value; _settings.DjCueOutputDeviceId = value?.Id; _saveSettings(); RestartOutput(); this.RaisePropertyChanged(); }
    }

    public DjViewModel(AppSettings settings, LibraryDatabase? library = null, Action? beforePlay = null,
        DjMixerEngine? mixer = null, DjCueStore? cues = null, Action? saveSettings = null, IDjSourceService? sources = null)
    {
        _settings = settings; _library = library; _beforePlay = beforePlay ?? (() => { });
        _saveSettings = saveSettings ?? (() => AppSettingsStore.Save(settings));
        _enabled = settings.DjModeEnabled;
        Mixer = mixer ?? new DjMixerEngine(); _cues = cues ?? new DjCueStore();
        Sources = sources ?? new DjSourceService();
        DeckA = new(this, 0); DeckB = new(this, 1);
        foreach (var deck in new[] { DeckA, DeckB }) deck.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(DjDeckViewModel.HasTrack) or nameof(DjDeckViewModel.Busy) or nameof(DjDeckViewModel.IsPlaying) or nameof(DjDeckViewModel.Title))
            {
                this.RaisePropertyChanged(nameof(HasSession)); this.RaisePropertyChanged(nameof(IsPlaying)); this.RaisePropertyChanged(nameof(SessionTitle));
                this.RaisePropertyChanged(nameof(PlayPauseLabel));
            }
        };
        Mixer.OutputFailed += (_, error) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _outputStarted = false; Status = "Audio output stopped: " + error;
        });
    }
    public void RefreshOptions() { if (!AiEnabled) AiTaskCancellation?.Cancel(); this.RaisePropertyChanged(nameof(AiEnabled)); }
    public void RefreshDevices()
    {
        try
        {
            var devices = Mixer.GetDevices();
            OutputDevices.Clear(); CueDevices.Clear();
            OutputDevices.Add(new("", "System default", true));
            CueDevices.Add(new("", "Headphones off", false));
            foreach (var device in devices) { OutputDevices.Add(device); CueDevices.Add(device); }
            _master = OutputDevices.FirstOrDefault(d => d.Id == (_settings.DjMasterOutputDeviceId ?? _settings.OutputDeviceId)) ?? OutputDevices[0];
            _headphones = CueDevices.FirstOrDefault(d => d.Id == _settings.DjCueOutputDeviceId) ?? CueDevices[0];
            this.RaisePropertyChanged(nameof(MasterDevice)); this.RaisePropertyChanged(nameof(HeadphoneDevice));
        }
        catch (Exception ex) { Status = ex.Message; }
    }
    public void RefreshLibrary()
    {
        var selected = SelectedTrack?.Path;
        LibraryTracks.Clear();
        var tracks = BrowserSource == "Queue" ? QueueSource?.Invoke() ?? [] :
            _library?.GetAllTracks().Where(t => File.Exists(t.SourcePath) || OpenUrlService.MightBeUrl(t.SourcePath))
                .Select(t => new DjLibraryTrack(t.SourcePath, t.DisplayTitle, t.Artist, t.Bpm?.ToString("0.#") ?? "—")) ?? [];
        foreach (var track in tracks.Where(t => $"{t.Title} {t.Artist}".Contains(Search, StringComparison.OrdinalIgnoreCase)).Take(250))
            LibraryTracks.Add(track);
        SelectedTrack = LibraryTracks.FirstOrDefault(t => t.Path == selected);
        this.RaisePropertyChanged(nameof(SelectedTrack));
    }
    public async Task SetEnabledAsync(bool enabled)
    {
        var activation = ++_activationVersion;
        _enabled = enabled;
        if (!enabled)
        {
            UseForVisualization = false;
            AiTaskCancellation?.Cancel();
            DeckA.CancelLoad(); DeckB.CancelLoad(); Mixer.PauseAll(); Mixer.StopOutput(); _outputStarted = false;
            try { await Mixer.StopRecordingAsync(); }
            catch (Exception ex) { Status = ex.Message; }
            if (activation != _activationVersion || _enabled) return;
            Mixer.UnloadAll();
            DeckA.Refresh(); DeckB.Refresh();
        }
        this.RaisePropertyChanged(nameof(Enabled));
        this.RaisePropertyChanged(nameof(HasSession));
    }
    public void EnsureOutput()
    {
        if (!_enabled) throw new InvalidOperationException("Enable DJ mode in Settings → Playback first.");
        _beforePlay();
        UseForVisualization = true;
        if (_outputStarted) return;
        if (_master is null) RefreshDevices();
        Mixer.StartOutput(string.IsNullOrEmpty(_master?.Id) ? null : _master.Id,
            string.IsNullOrEmpty(_headphones?.Id) ? null : _headphones.Id);
        _outputStarted = true;
    }
    public void TogglePlayback() => Run(() =>
    {
        if (Mixer.GetDeck(0).Playing || Mixer.GetDeck(1).Playing)
        {
            _resumeDecks = [Mixer.GetDeck(0).Playing, Mixer.GetDeck(1).Playing]; Mixer.PauseAll();
        }
        else
        {
            if (!DeckA.HasTrack && !DeckB.HasTrack) return;
            EnsureOutput();
            var resumeAll = !_resumeDecks.Any(b => b);
            for (var i = 0; i < 2; i++)
                if ((resumeAll || _resumeDecks[i]) && Mixer.GetDeck(i).Path is not null)
                    Mixer.Transport(i, DjTransport.PlayPause);
        }
    });
    public void StopPlayback() => Run(() =>
    {
        Mixer.Transport(0, DjTransport.Stop); Mixer.Transport(1, DjTransport.Stop); _resumeDecks = [false, false];
    });
    public void ToggleMute()
    {
        if (MasterVolume > 0) { _unmutedVolume = MasterVolume; MasterVolume = 0; }
        else MasterVolume = _unmutedVolume;
    }
    private void RestartOutput()
    {
        if (!_outputStarted) return;
        Mixer.PauseAll(); _outputStarted = false;
        Run(EnsureOutput);
    }
    public void Run(Action action)
    {
        try { action(); DeckA.Refresh(); DeckB.Refresh(); }
        catch (Exception ex) { Status = ex.Message; }
    }
    public void SaveCues(int deck) { try { _cues.Save(Mixer.GetDeck(deck)); } catch (Exception ex) { Status = "Could not save cue points: " + ex.Message; } }
    public void RestoreCues(int deck, string path)
    {
        if (_cues.Get(path) is not { } data) return;
        Mixer.RestoreCues(deck, data.Cue, data.HotCues);
        Mixer.ConfigureDeck(deck, Mixer.GetDeck(deck).Settings with { Bpm = data.Bpm, BeatOffset = data.BeatOffset });
    }
    public void Tick()
    {
        DeckA.Refresh(); DeckB.Refresh();
        this.RaisePropertyChanged(nameof(TempoDifference));
        this.RaisePropertyChanged(nameof(MasterPeak)); this.RaisePropertyChanged(nameof(Clipping));
        if (Mixer.RecordingError is { } error) Status = error;
        this.RaisePropertyChanged(nameof(IsRecording)); this.RaisePropertyChanged(nameof(RecordingLabel));
    }
    public void Dispose()
    {
        AiTaskCancellation?.Cancel();
        DeckA.CancelLoad(); DeckB.CancelLoad();
        try { Mixer.Dispose(); } catch (Exception ex) { SpectralisLog.Warn("DJ shutdown: " + ex.Message); }
    }
}

public sealed record DjLibraryTrack(string Path, string Title, string Artist, string Bpm);

public sealed class DjDeckViewModel : ViewModelBase
{
    private readonly DjViewModel _owner;
    private CancellationTokenSource? _load;
    private DjDeckState _state;
    private bool _busy;
    private string _sourceLabel = "Local files or URL";
    private string _title = "No track loaded", _artist = "Drop audio here or choose Load audio";
    private double _loopBeats = 4;
    public int Index { get; }
    public string Label => Index == 0 ? "DECK A" : "DECK B";
    public bool IsSelected { get => _owner.ActiveDeckIndex == Index; set { if (value) _owner.ActiveDeckIndex = Index; } }
    public void RefreshSelection() => this.RaisePropertyChanged(nameof(IsSelected));
    public bool IsPlaying => _state.Playing;
    public string SourceLabel { get => _sourceLabel; private set => this.RaiseAndSetIfChanged(ref _sourceLabel, value); }
    public string Title { get => _title; private set => this.RaiseAndSetIfChanged(ref _title, value); }
    public string Artist { get => _artist; private set => this.RaiseAndSetIfChanged(ref _artist, value); }
    public string? Path => _state.Path;
    public bool HasTrack => _state.Path is not null;
    public bool Busy { get => _busy; private set { this.RaiseAndSetIfChanged(ref _busy, value); this.RaisePropertyChanged(nameof(CanLoad)); } }
    public bool CanLoad => !Busy && !_state.Playing;
    public string PlayLabel => _state.Playing ? "Pause" : "Play";
    public double Position { get => _state.Position; set => _owner.Run(() => _owner.Mixer.Seek(Index, value)); }
    public double Duration => _state.Duration;
    public string TimeText => $"{TimeSpan.FromSeconds(Position):mm\\:ss\\.ff} / {TimeSpan.FromSeconds(Duration):mm\\:ss}";
    public string PreviewDetails => !HasTrack ? "Waveform, tempo, key, beat position and remaining time" :
        $"{EffectiveBpm:0.0} BPM · {Key} · beat {Math.Max(0, (Position - BeatOffset) * Bpm / 60) + 1:0.0}\n{TimeSpan.FromSeconds(Math.Max(0, Duration - Position)):mm\\:ss} left · {Stems.Count} stems · {LoopText}";
    public float[] Waveform => _state.Waveform;
    public double Peak => _state.Peak * 100;
    public string Key => string.IsNullOrEmpty(_state.Key) ? "—" : _state.Key;
    public double EffectiveBpm => Bpm * (1 + TempoPercent / 100);
    public string LoopText => _state.LoopEnabled ? $"LOOP {_state.LoopIn:0.00}–{_state.LoopOut:0.00}s" : "Loop off";
    public double? LoopStart => _state.LoopEnabled ? _state.LoopIn : null;
    public double? LoopEnd => _state.LoopEnabled ? _state.LoopOut : null;
    public double[] LoopOptions { get; } = [.25, .5, 1, 2, 4, 8, 16, 32];
    public double LoopBeats { get => _loopBeats; set => this.RaiseAndSetIfChanged(ref _loopBeats, value); }
    public ObservableCollection<DjStemViewModel> Stems { get; } = [];
    public ObservableCollection<DjHotCueViewModel> HotCues { get; } = [];
    private void Configure(Func<DjDeckSettings, DjDeckSettings> change)
    {
        _owner.Run(() => _owner.Mixer.ConfigureDeck(Index, change(_owner.Mixer.GetDeck(Index).Settings)));
    }
    public double TempoPercent { get => _state.Settings.TempoPercent; set => Configure(s => s with { TempoPercent = value }); }
    public double Bpm { get => _state.Settings.Bpm; set { Configure(s => s with { Bpm = value }); _owner.SaveCues(Index); } }
    public double BeatOffset { get => _state.Settings.BeatOffset; set { Configure(s => s with { BeatOffset = value }); _owner.SaveCues(Index); } }
    public bool KeyLock { get => _state.Settings.KeyLock; set => Configure(s => s with { KeyLock = value }); }
    public bool Quantize { get => _state.Settings.Quantize; set => Configure(s => s with { Quantize = value }); }
    public bool HeadphoneCue { get => _state.Settings.HeadphoneCue; set => Configure(s => s with { HeadphoneCue = value }); }
    public double GainDb { get => _state.Settings.GainDb; set => Configure(s => s with { GainDb = value }); }
    public double Fader { get => _state.Settings.Fader * 100; set => Configure(s => s with { Fader = value / 100 }); }
    public double LowDb { get => _state.Settings.LowDb; set => Configure(s => s with { LowDb = value }); }
    public double MidDb { get => _state.Settings.MidDb; set => Configure(s => s with { MidDb = value }); }
    public double HighDb { get => _state.Settings.HighDb; set => Configure(s => s with { HighDb = value }); }
    public double Filter { get => _state.Settings.Filter; set => Configure(s => s with { Filter = value }); }
    public ReactiveCommand<Unit, Unit> PlayCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<Unit, Unit> CueCommand { get; }
    public ReactiveCommand<Unit, Unit> SetCueCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncCommand { get; }
    public ReactiveCommand<Unit, Unit> BeatLoopCommand { get; }
    public ReactiveCommand<Unit, Unit> LoopInCommand { get; }
    public ReactiveCommand<Unit, Unit> LoopOutCommand { get; }
    public ReactiveCommand<Unit, Unit> LoopToggleCommand { get; }
    public ReactiveCommand<Unit, Unit> HalfLoopCommand { get; }
    public ReactiveCommand<Unit, Unit> DoubleLoopCommand { get; }
    public ReactiveCommand<Unit, Unit> JumpBackCommand { get; }
    public ReactiveCommand<Unit, Unit> JumpForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeBackCommand { get; }
    public ReactiveCommand<Unit, Unit> NudgeForwardCommand { get; }
    public ReactiveCommand<Unit, Unit> ResetMixerCommand { get; }
    public ReactiveCommand<Unit, Unit> SetDownbeatCommand { get; }

    public DjDeckViewModel(DjViewModel owner, int index)
    {
        _owner = owner; Index = index; _state = owner.Mixer.GetDeck(index);
        ReactiveCommand<Unit, Unit> Command(Action action) => ReactiveCommand.Create(() => owner.Run(action));
        ReactiveCommand<Unit, Unit> Transport(DjTransport action) => Command(() => { owner.Mixer.Transport(index, action); owner.SaveCues(index); });
        PlayCommand = Command(() => { if (Busy || !HasTrack) return; owner.EnsureOutput(); owner.Mixer.Transport(index, DjTransport.PlayPause); });
        StopCommand = Transport(DjTransport.Stop); CueCommand = Transport(DjTransport.Cue); SetCueCommand = Transport(DjTransport.SetCue);
        SyncCommand = Command(() => owner.Mixer.Sync(index));
        BeatLoopCommand = Command(() => owner.Mixer.BeatLoop(index, LoopBeats));
        LoopInCommand = Transport(DjTransport.LoopIn); LoopOutCommand = Transport(DjTransport.LoopOut);
        LoopToggleCommand = Transport(DjTransport.ToggleLoop); HalfLoopCommand = Transport(DjTransport.HalfLoop); DoubleLoopCommand = Transport(DjTransport.DoubleLoop);
        JumpBackCommand = Command(() => owner.Mixer.BeatJump(index, -4)); JumpForwardCommand = Command(() => owner.Mixer.BeatJump(index, 4));
        NudgeBackCommand = Command(() => owner.Mixer.Nudge(index, -.04)); NudgeForwardCommand = Command(() => owner.Mixer.Nudge(index, .04));
        ResetMixerCommand = Command(() => Configure(s => s with { LowDb = 0, MidDb = 0, HighDb = 0, GainDb = 0, Filter = 0, Fader = 1, TempoPercent = 0 }));
        SetDownbeatCommand = Command(() => { BeatOffset = Position; });
        for (var i = 0; i < 4; i++) HotCues.Add(new(owner, index, i));
    }
    public void CancelLoad() => _load?.Cancel();
    public Task LoadFilesAsync(IReadOnlyList<string> paths) => LoadSourcesAsync(paths.Select(path => new DjSource(path)).ToArray());
    public Task LoadSourcesAsync(IReadOnlyList<DjSource> sources) => LoadCoreAsync(
        sources.Select(source => new DjStemInput(source.Reference, System.IO.Path.GetFileNameWithoutExtension(source.Reference))).ToArray(), null, sources);
    public Task LoadAsync(IReadOnlyList<DjStemInput> inputs, string? identityPath = null) => LoadCoreAsync(inputs, identityPath, null);
    private async Task LoadCoreAsync(IReadOnlyList<DjStemInput> inputs, string? identityPath, IReadOnlyList<DjSource>? sources)
    {
        if (!_owner.Enabled || inputs.Count == 0) return;
        if (!CanLoad) { _owner.Status = $"{Label}: pause this deck or wait for its current load to finish."; return; }
        CancelLoad();
        using var cancellation = new CancellationTokenSource(); _load = cancellation;
        Busy = true; _owner.Status = $"{Label}: inspecting audio…";
        DjTrack? track = null;
        var prepared = new List<DjPreparedSource>();
        try
        {
            if (inputs.Count > 8) throw new ArgumentException("Choose up to eight aligned stems for one deck.");
            if (sources is not null)
            {
                foreach (var source in sources)
                    prepared.Add(await _owner.Sources.PrepareAsync(source, new Progress<string>(message =>
                    { if (!cancellation.IsCancellationRequested && Busy) _owner.Status = $"{Label}: {message}"; }), cancellation.Token));
                inputs = await DjTrackLoader.ResolveAsync(prepared.Select(p => p.Path).ToArray(), cancellation.Token);
                identityPath = prepared[0].Identity;
                if (inputs.Count == prepared.Count)
                    inputs = inputs.Select((input, i) => prepared[i].Title is { Length: > 0 } title && !File.Exists(prepared[i].Identity)
                        ? input with { Name = title } : input).ToArray();
            }
            _owner.Status = $"{Label}: decoding and analyzing…";
            track = await DjTrack.LoadAsync(inputs, cancellation.Token, identityPath);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!_owner.Enabled) return;
            _owner.Mixer.LoadDeck(Index, track); track = null;
            _owner.RestoreCues(Index, identityPath ?? inputs[0].Path);
            var metadata = TrackMetadataReader.Read(identityPath is not null && File.Exists(identityPath) ? identityPath : inputs[0].Path);
            var title = prepared.FirstOrDefault()?.Title ?? metadata.DisplayTitle;
            Title = inputs.Count > 1 ? title + " · stem set" : title;
            Artist = inputs.Count > 1 ? "Aligned stems · BPM/key from the first stem; adjust the grid as needed" : prepared.FirstOrDefault()?.Artist ?? metadata.Artist;
            SourceLabel = prepared.FirstOrDefault()?.Service ?? "Prepared stems";
            Stems.Clear();
            for (var i = 0; i < inputs.Count; i++) Stems.Add(new(_owner, Index, i, inputs[i].Name));
            _owner.Status = $"{Label} ready. Cue points and beat grid are saved with this track.";
        }
        catch (OperationCanceledException) { _owner.Status = $"{Label}: loading cancelled."; }
        catch (Exception ex) { _owner.Status = $"{Label}: {ex.Message}"; }
        finally { track?.Dispose(); foreach (var source in prepared) source.Dispose(); if (ReferenceEquals(_load, cancellation)) _load = null; Busy = false; Refresh(); }
    }
    public void Refresh()
    {
        var previous = _state;
        _state = _owner.Mixer.GetDeck(Index);
        if (previous.Playing != _state.Playing) this.RaisePropertyChanged(nameof(IsPlaying));
        if (previous.Path != _state.Path) this.RaisePropertyChanged(nameof(HasTrack));
        if (!HasTrack)
        {
            Title = "No track loaded";
            Artist = "Drop audio here or choose Load audio";
            SourceLabel = "Local files or URL";
            if (Stems.Count != 0) Stems.Clear();
        }
        foreach (var property in new[] { nameof(CanLoad), nameof(PlayLabel), nameof(Position), nameof(Duration), nameof(TimeText), nameof(PreviewDetails),
            nameof(Waveform), nameof(Peak), nameof(Key), nameof(EffectiveBpm), nameof(LoopText), nameof(LoopStart), nameof(LoopEnd),
            nameof(TempoPercent), nameof(Bpm), nameof(BeatOffset), nameof(KeyLock), nameof(Quantize), nameof(HeadphoneCue), nameof(GainDb),
            nameof(Fader), nameof(LowDb), nameof(MidDb), nameof(HighDb), nameof(Filter) }) this.RaisePropertyChanged(property);
        for (var i = 0; i < 4; i++) HotCues[i].Update(_state.HotCues[i]);
    }
}

public sealed class DjStemViewModel : ViewModelBase
{
    private readonly DjViewModel _owner;
    private readonly int _deck, _index;
    private double _volume = 100;
    private bool _muted, _solo;
    public string Name { get; }
    public double Volume { get => _volume; set { this.RaiseAndSetIfChanged(ref _volume, value); Apply(); } }
    public bool Muted { get => _muted; set { this.RaiseAndSetIfChanged(ref _muted, value); Apply(); } }
    public bool Solo { get => _solo; set { this.RaiseAndSetIfChanged(ref _solo, value); Apply(); } }
    public DjStemViewModel(DjViewModel owner, int deck, int index, string name) { _owner = owner; _deck = deck; _index = index; Name = name; }
    private void Apply() => _owner.Mixer.SetStem(_deck, _index, _volume / 100, _muted, _solo);
}

public sealed class DjHotCueViewModel : ViewModelBase
{
    private readonly int _slot;
    private double? _time;
    public string Label => _time is { } time ? $"{_slot + 1} · {TimeSpan.FromSeconds(time):mm\\:ss}" : $"{_slot + 1} · SET";
    public ReactiveCommand<Unit, Unit> TriggerCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public DjHotCueViewModel(DjViewModel owner, int deck, int slot)
    {
        _slot = slot;
        TriggerCommand = ReactiveCommand.Create(() => owner.Run(() => { owner.Mixer.HotCue(deck, slot); owner.SaveCues(deck); }));
        ClearCommand = ReactiveCommand.Create(() => owner.Run(() => { owner.Mixer.HotCue(deck, slot, true); owner.SaveCues(deck); }));
    }
    public void Update(double? time) { if (_time == time) return; _time = time; this.RaisePropertyChanged(nameof(Label)); }
}
