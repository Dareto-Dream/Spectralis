using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.App.Services;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.ViewModels;

/// <summary>One draggable node on the EQ "envelope" — a single parametric band.</summary>
public sealed class EqBandViewModel : ViewModelBase
{
    private readonly EqEditorViewModel _owner;
    private EqBand _band;

    public EqBandViewModel(EqEditorViewModel owner, int index, EqBand band)
    {
        _owner = owner;
        Index = index;
        _band = band;
    }

    public int Index { get; }

    public IReadOnlyList<EqFilterType> FilterTypes { get; } = Enum.GetValues<EqFilterType>();

    public EqBand Model => _band;

    public double Frequency
    {
        get => _band.Frequency;
        set => Apply(_band with { Frequency = (float)Math.Clamp(value, 20, 20000) });
    }

    public double GainDb
    {
        get => _band.GainDb;
        set => Apply(_band with { GainDb = (float)Math.Clamp(value, -24, 24) });
    }

    public double Q
    {
        get => _band.Q;
        set => Apply(_band with { Q = (float)Math.Clamp(value, 0.1, 18) });
    }

    public EqFilterType Type
    {
        get => _band.Type;
        set => Apply(_band with { Type = value });
    }

    public bool Enabled
    {
        get => _band.Enabled;
        set => Apply(_band with { Enabled = value });
    }

    public bool GainMatters => _band.Type is EqFilterType.Peak or EqFilterType.LowShelf or EqFilterType.HighShelf;

    public string Label => Frequency >= 1000 ? $"{Frequency / 1000:0.##} kHz" : $"{Frequency:0} Hz";

    private void Apply(EqBand next)
    {
        if (next == _band)
        {
            return;
        }

        _band = next;
        _owner.WriteBand(Index, next);
        this.RaisePropertyChanged(nameof(Frequency));
        this.RaisePropertyChanged(nameof(GainDb));
        this.RaisePropertyChanged(nameof(Q));
        this.RaisePropertyChanged(nameof(Type));
        this.RaisePropertyChanged(nameof(Enabled));
        this.RaisePropertyChanged(nameof(GainMatters));
        this.RaisePropertyChanged(nameof(Label));
    }
}

/// <summary>
/// View-model behind the parametric-EQ envelope editor: the band nodes, preamp /
/// output trims, and the preset list (built-ins from <see cref="EqPresets"/> plus
/// user presets from <see cref="EqPresetStore"/>).
/// </summary>
public sealed class EqEditorViewModel : ViewModelBase
{
    public const string CustomLabel = "Custom";

    private readonly ParametricEqEffect _effect;
    private readonly Action _onEdited;
    private string _selectedPresetName = EqPresets.FlatName;
    private string _newPresetName = string.Empty;
    private bool _suppressPresetApply;

    public EqEditorViewModel(ParametricEqEffect effect, Action onEdited)
    {
        _effect = effect;
        _onEdited = onEdited;
        ReloadPresets();
        ReloadBands();
    }

    public ParametricEqEffect Effect => _effect;

    public ObservableCollection<EqBandViewModel> Bands { get; } = new();

    public ObservableCollection<string> PresetNames { get; } = new();

    /// <summary>Raised whenever the curve changes — the <c>EqCurveEditor</c> redraws on this.</summary>
    public event EventHandler? CurveChanged;

    public string SelectedPresetName
    {
        get => _selectedPresetName;
        set
        {
            if (_selectedPresetName == value || value is null)
            {
                return;
            }

            _selectedPresetName = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(CanDeleteSelectedPreset));
            if (!_suppressPresetApply)
            {
                ApplyPreset(value);
            }
        }
    }

    public string NewPresetName
    {
        get => _newPresetName;
        set => this.RaiseAndSetIfChanged(ref _newPresetName, value);
    }

    public bool CanDeleteSelectedPreset =>
        _selectedPresetName != CustomLabel &&
        !EqPresets.BuiltIn.Any(p => string.Equals(p.Name, _selectedPresetName, StringComparison.OrdinalIgnoreCase));

    public double PreampDb
    {
        get => _effect.PreampDb;
        set
        {
            if (Math.Abs(_effect.PreampDb - value) < 0.001)
            {
                return;
            }

            _effect.PreampDb = (float)value;
            this.RaisePropertyChanged();
            MarkCustom();
            NotifyEdited();
        }
    }

    public double OutputGainDb
    {
        get => _effect.OutputGainDb;
        set
        {
            if (Math.Abs(_effect.OutputGainDb - value) < 0.001)
            {
                return;
            }

            _effect.OutputGainDb = (float)value;
            this.RaisePropertyChanged();
            MarkCustom();
            NotifyEdited();
        }
    }

    public bool CanAddBand => Bands.Count < ParametricEqEffect.MaxBands;

    public bool CanRemoveBand => Bands.Count > ParametricEqEffect.MinBands;

    internal void WriteBand(int index, EqBand band)
    {
        _effect.SetBand(index, band);
        MarkCustom();
        NotifyEdited();
    }

    public void AddBandAt(double frequencyHz, double gainDb)
    {
        if (!CanAddBand)
        {
            return;
        }

        var bands = _effect.ReadBands().ToList();
        bands.Add(new EqBand(
            (float)Math.Clamp(frequencyHz, 20, 20000),
            (float)Math.Clamp(gainDb, -24, 24),
            1.0f,
            EqFilterType.Peak));
        _effect.WriteBands(bands);
        ReloadBands();
        MarkCustom();
        NotifyEdited();
    }

    public void RemoveBand(EqBandViewModel band)
    {
        if (!CanRemoveBand)
        {
            return;
        }

        var bands = _effect.ReadBands().ToList();
        if (band.Index < 0 || band.Index >= bands.Count)
        {
            return;
        }

        bands.RemoveAt(band.Index);
        _effect.WriteBands(bands);
        ReloadBands();
        MarkCustom();
        NotifyEdited();
    }

    public void ResetToFlat() => ApplyPreset(EqPresets.FlatName);

    public void ApplyPreset(string name)
    {
        var preset = EqPresets.BuiltIn.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                     ?? EqPresetStore.Load().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (preset is null)
        {
            return;
        }

        preset.ApplyTo(_effect);
        SetSelectedPresetSilently(preset.Name);
        ReloadBands();
        this.RaisePropertyChanged(nameof(PreampDb));
        this.RaisePropertyChanged(nameof(OutputGainDb));
        NotifyEdited();
    }

    public void SaveCurrentAsPreset(string name)
    {
        name = (name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (EqPresets.BuiltIn.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(name, CustomLabel, StringComparison.OrdinalIgnoreCase))
        {
            name += " (custom)";
        }

        EqPresetStore.AddOrReplace(EqPreset.FromEffect(_effect, name));
        ReloadPresets();
        SetSelectedPresetSilently(name);
        NewPresetName = string.Empty;
    }

    public void DeleteSelectedPreset()
    {
        if (!CanDeleteSelectedPreset)
        {
            return;
        }

        EqPresetStore.Delete(_selectedPresetName);
        ReloadPresets();
        SetSelectedPresetSilently(CustomLabel);
    }

    /// <summary>Log-spaced combined response (dB) sampled across the audible band, for the curve renderer.</summary>
    public double[] ComputeResponseCurve(int points, double minHz, double maxHz, int sampleRate = 48000)
    {
        var result = new double[points];
        var logMin = Math.Log10(minHz);
        var logMax = Math.Log10(maxHz);
        for (var i = 0; i < points; i++)
        {
            var hz = Math.Pow(10, logMin + ((logMax - logMin) * i / (points - 1)));
            result[i] = _effect.ResponseDb(hz, sampleRate);
        }

        return result;
    }

    private void SetSelectedPresetSilently(string name)
    {
        _suppressPresetApply = true;
        SelectedPresetName = name;
        _suppressPresetApply = false;
    }

    private void MarkCustom()
    {
        if (_selectedPresetName != CustomLabel)
        {
            SetSelectedPresetSilently(CustomLabel);
        }
    }

    private void NotifyEdited()
    {
        this.RaisePropertyChanged(nameof(CanAddBand));
        this.RaisePropertyChanged(nameof(CanRemoveBand));
        _onEdited();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadBands()
    {
        Bands.Clear();
        var bands = _effect.ReadBands();
        for (var i = 0; i < bands.Count; i++)
        {
            Bands.Add(new EqBandViewModel(this, i, bands[i]));
        }

        this.RaisePropertyChanged(nameof(CanAddBand));
        this.RaisePropertyChanged(nameof(CanRemoveBand));
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadPresets()
    {
        PresetNames.Clear();
        PresetNames.Add(CustomLabel);
        foreach (var preset in EqPresets.BuiltIn)
        {
            PresetNames.Add(preset.Name);
        }

        foreach (var preset in EqPresetStore.Load())
        {
            PresetNames.Add(preset.Name);
        }
    }
}
