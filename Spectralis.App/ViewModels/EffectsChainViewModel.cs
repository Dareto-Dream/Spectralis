using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.ViewModels;

/// <summary>One labeled slider for a single effect parameter.</summary>
public sealed class EffectParamViewModel : ViewModelBase
{
    private readonly EffectParameters _parameters;
    private readonly string _key;
    private readonly Action _onChanged;
    private readonly string _format;

    public EffectParamViewModel(
        string label,
        string key,
        float min,
        float max,
        EffectParameters parameters,
        Action onChanged,
        string format = "0.##")
    {
        Label = label;
        Minimum = min;
        Maximum = max;
        _key = key;
        _parameters = parameters;
        _onChanged = onChanged;
        _format = format;
    }

    public string Label { get; }
    public double Minimum { get; }
    public double Maximum { get; }

    public double Value
    {
        get => _parameters.Get(_key);
        set
        {
            var clamped = (float)Math.Clamp(value, Minimum, Maximum);
            if (Math.Abs(_parameters.Get(_key) - clamped) < 0.0001f)
            {
                return;
            }

            _parameters.Set(_key, clamped);
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(ValueText));
            _onChanged();
        }
    }

    public string ValueText => Value.ToString(_format);
}

/// <summary>One rack slot: an effect with its enable toggle and parameter sliders.</summary>
public sealed class EffectItemViewModel : ViewModelBase
{
    private readonly IAudioEffect _effect;
    private readonly Action _onChanged;

    public EffectItemViewModel(IAudioEffect effect, Action onChanged, Action? onStateChanged = null)
    {
        _effect = effect;
        _onChanged = onChanged;

        if (effect is ParametricEqEffect eq)
        {
            // The parametric EQ has its own envelope editor instead of a slider bank.
            // Its edits are picked up per audio block via EffectParameters.Revision, so
            // they only need the persistence hook, not a full engine rebuild.
            EqEditor = new EqEditorViewModel(eq, () => onStateChanged?.Invoke());
            Sliders = new ObservableCollection<EffectParamViewModel>();
        }
        else
        {
            Sliders = new ObservableCollection<EffectParamViewModel>(BuildSliders(effect, () =>
            {
                onChanged();
                onStateChanged?.Invoke();
            }));
        }
    }

    public IAudioEffect Effect => _effect;

    /// <summary>Non-null only for the parametric EQ; drives the envelope editor UI.</summary>
    public EqEditorViewModel? EqEditor { get; }

    public bool HasEqEditor => EqEditor is not null;

    public string Name => _effect.Name;

    public bool Enabled
    {
        get => _effect.Enabled;
        set
        {
            if (_effect.Enabled == value)
            {
                return;
            }

            _effect.Enabled = value;
            this.RaisePropertyChanged();
            _onChanged();
        }
    }

    public ObservableCollection<EffectParamViewModel> Sliders { get; }

    private static IEnumerable<EffectParamViewModel> BuildSliders(IAudioEffect effect, Action onChanged)
    {
        switch (effect)
        {
            case CompressorEffect:
                yield return new EffectParamViewModel("Threshold (dBFS)", "threshold", -60, 0, effect.Parameters, onChanged, "0.0");
                yield return new EffectParamViewModel("Ratio", "ratio", 1, 20, effect.Parameters, onChanged, "0.0");
                yield return new EffectParamViewModel("Attack (ms)", "attack", 0.1f, 500, effect.Parameters, onChanged, "0.0");
                yield return new EffectParamViewModel("Release (ms)", "release", 10, 5000, effect.Parameters, onChanged, "0");
                yield return new EffectParamViewModel("Makeup (dB)", "makeup", -12, 24, effect.Parameters, onChanged, "0.0");
                break;

            case ReverbEffect:
                yield return new EffectParamViewModel("Room Size", "roomSize", 0, 1, effect.Parameters, onChanged);
                yield return new EffectParamViewModel("Damping", "damping", 0, 1, effect.Parameters, onChanged);
                yield return new EffectParamViewModel("Wet", "wet", 0, 1, effect.Parameters, onChanged);
                break;

            case VocalBlendEffect:
                yield return new EffectParamViewModel("Vocal Remove", "blend", 0, 1, effect.Parameters, onChanged);
                break;
        }
    }
}

public sealed class EffectsChainViewModel : ViewModelBase
{
    private readonly EffectChain _chain;
    private readonly Action? _onStateChanged;
    private EffectItemViewModel? _selectedEffect;
    private string _selectedNewEffect;

    public EffectsChainViewModel(EffectChain chain, Action? onStateChanged = null)
    {
        _chain = chain;
        _onStateChanged = onStateChanged;
        _selectedNewEffect = EffectChain.AvailableEffects[0];
        Reload();
    }

    /// <summary>True when the selected rack slot is the parametric EQ (widens the sidebar for the curve editor).</summary>
    public bool IsEqSelected => _selectedEffect?.HasEqEditor == true;

    public ObservableCollection<EffectItemViewModel> EffectItems { get; } = new();

    public IReadOnlyList<string> AvailableEffects => EffectChain.AvailableEffects;

    public string SelectedNewEffect
    {
        get => _selectedNewEffect;
        set => this.RaiseAndSetIfChanged(ref _selectedNewEffect, value);
    }

    public EffectItemViewModel? SelectedEffect
    {
        get => _selectedEffect;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedEffect, value);
            this.RaisePropertyChanged(nameof(IsEqSelected));
        }
    }

    public bool ChainEnabled
    {
        get => _chain.Enabled;
        set
        {
            if (_chain.Enabled == value)
            {
                return;
            }

            _chain.Enabled = value;
            this.RaisePropertyChanged();
            _chain.NotifyChanged();
        }
    }

    public void AddSelectedEffect()
    {
        if (string.IsNullOrWhiteSpace(SelectedNewEffect))
        {
            return;
        }

        var effect = EffectChain.CreateEffect(SelectedNewEffect);
        _chain.Add(effect);
        Reload();
        SelectedEffect = EffectItems.LastOrDefault();
        _onStateChanged?.Invoke();
    }

    public void RemoveSelectedEffect()
    {
        if (SelectedEffect is null)
        {
            return;
        }

        _chain.Remove(SelectedEffect.Effect);
        Reload();
        _onStateChanged?.Invoke();
    }

    public void MoveSelectedEffectUp()
    {
        var index = IndexOfSelected();
        if (index <= 0)
        {
            return;
        }

        _chain.MoveUp(index);
        Reload();
        SelectedEffect = EffectItems[index - 1];
        _onStateChanged?.Invoke();
    }

    public void MoveSelectedEffectDown()
    {
        var index = IndexOfSelected();
        if (index < 0 || index >= EffectItems.Count - 1)
        {
            return;
        }

        _chain.MoveDown(index);
        Reload();
        SelectedEffect = EffectItems[index + 1];
        _onStateChanged?.Invoke();
    }

    private int IndexOfSelected()
    {
        for (var index = 0; index < EffectItems.Count; index++)
        {
            if (ReferenceEquals(EffectItems[index], SelectedEffect))
            {
                return index;
            }
        }

        return -1;
    }

    private void Reload()
    {
        EffectItems.Clear();
        foreach (var effect in _chain.Effects)
        {
            EffectItems.Add(new EffectItemViewModel(effect, _chain.NotifyChanged, _onStateChanged));
        }

        this.RaisePropertyChanged(nameof(IsEqSelected));
    }
}
