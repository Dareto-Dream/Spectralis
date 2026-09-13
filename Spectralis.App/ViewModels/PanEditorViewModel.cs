using System.Collections.ObjectModel;
using ReactiveUI;
using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.ViewModels;

/// <summary>One draggable node on the pan-automation loop.</summary>
public sealed class PanPointViewModel : ViewModelBase
{
    private readonly PanEditorViewModel _owner;
    private PanPoint _point;

    public PanPointViewModel(PanEditorViewModel owner, int index, PanPoint point)
    {
        _owner = owner;
        Index = index;
        _point = point;
    }

    public int Index { get; }

    public PanPoint Model => _point;

    /// <summary>Normalized position within the loop, 0-1.</summary>
    public double Time
    {
        get => _point.Time;
        set => Apply(_point with { Time = (float)Math.Clamp(value, 0, 1) });
    }

    /// <summary>-1 (left) .. 1 (right).</summary>
    public double Pan
    {
        get => _point.Pan;
        set => Apply(_point with { Pan = (float)Math.Clamp(value, -1, 1) });
    }

    private void Apply(PanPoint next)
    {
        if (next == _point)
        {
            return;
        }

        _point = next;
        _owner.WritePoint(Index, next);
        this.RaisePropertyChanged(nameof(Time));
        this.RaisePropertyChanged(nameof(Pan));
    }
}

/// <summary>
/// View-model behind the stereo panner's auto-pan curve editor: the loop nodes plus
/// the auto-pan toggle and bars/BPM loop-length controls. Edits go straight to the
/// effect's <see cref="EffectParameters"/> and only call the persistence hook
/// (<c>onEdited</c>) — never the chain's rebuild-the-engine notification — so
/// dragging a node doesn't tear down playback, same trick <c>EqEditorViewModel</c> uses.
/// </summary>
public sealed class PanEditorViewModel : ViewModelBase
{
    private readonly StereoPannerEffect _effect;
    private readonly Action _onEdited;

    public PanEditorViewModel(StereoPannerEffect effect, Action onEdited)
    {
        _effect = effect;
        _onEdited = onEdited;
        ReloadPoints();
    }

    public StereoPannerEffect Effect => _effect;

    public ObservableCollection<PanPointViewModel> Points { get; } = new();

    /// <summary>Raised whenever the curve or loop settings change — the curve editor control redraws on this.</summary>
    public event EventHandler? CurveChanged;

    public bool AutoPanEnabled
    {
        get => _effect.AutoPanEnabled;
        set
        {
            if (_effect.AutoPanEnabled == value)
            {
                return;
            }

            _effect.AutoPanEnabled = value;
            this.RaisePropertyChanged();
            NotifyEdited();
        }
    }

    public int Bars
    {
        get => _effect.Bars;
        set
        {
            var clamped = Math.Clamp(value, 1, 16);
            if (_effect.Bars == clamped)
            {
                return;
            }

            _effect.Bars = clamped;
            this.RaisePropertyChanged();
            NotifyEdited();
        }
    }

    public double Bpm
    {
        get => _effect.Bpm;
        set
        {
            var clamped = (float)Math.Clamp(value, 40, 220);
            if (Math.Abs(_effect.Bpm - clamped) < 0.01f)
            {
                return;
            }

            _effect.Bpm = clamped;
            this.RaisePropertyChanged();
            NotifyEdited();
        }
    }

    public bool CanAddPoint => Points.Count < StereoPannerEffect.MaxPoints;

    public bool CanRemovePoint => Points.Count > StereoPannerEffect.MinPoints;

    internal void WritePoint(int index, PanPoint point)
    {
        _effect.SetPoint(index, point);
        NotifyEdited();
    }

    public void AddPointAt(double time, double pan)
    {
        if (!CanAddPoint)
        {
            return;
        }

        var points = _effect.ReadPoints().ToList();
        points.Add(new PanPoint((float)Math.Clamp(time, 0, 1), (float)Math.Clamp(pan, -1, 1)));
        _effect.WritePoints(points);
        ReloadPoints();
        NotifyEdited();
    }

    public void RemovePoint(PanPointViewModel point)
    {
        if (!CanRemovePoint)
        {
            return;
        }

        var points = _effect.ReadPoints().ToList();
        if (point.Index < 0 || point.Index >= points.Count)
        {
            return;
        }

        points.RemoveAt(point.Index);
        _effect.WritePoints(points);
        ReloadPoints();
        NotifyEdited();
    }

    public void ResetToDefault()
    {
        _effect.WritePoints(StereoPannerEffect.DefaultPoints);
        ReloadPoints();
        NotifyEdited();
    }

    /// <summary>Sampled pan curve across one full loop (0-1), for the curve renderer.</summary>
    public double[] ComputeCurve(int points)
    {
        var result = new double[points];
        var model = Points.Select(p => p.Model).ToArray();
        for (var i = 0; i < points; i++)
        {
            var phase = (float)i / (points - 1);
            result[i] = StereoPannerEffect.EvaluatePan(model, phase);
        }

        return result;
    }

    private void NotifyEdited()
    {
        this.RaisePropertyChanged(nameof(CanAddPoint));
        this.RaisePropertyChanged(nameof(CanRemovePoint));
        _onEdited();
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ReloadPoints()
    {
        Points.Clear();
        var points = _effect.ReadPoints();
        for (var i = 0; i < points.Count; i++)
        {
            Points.Add(new PanPointViewModel(this, i, points[i]));
        }

        this.RaisePropertyChanged(nameof(CanAddPoint));
        this.RaisePropertyChanged(nameof(CanRemovePoint));
        CurveChanged?.Invoke(this, EventArgs.Empty);
    }
}
