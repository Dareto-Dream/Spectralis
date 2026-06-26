using System.Text.Json;

namespace Spectralis.Core.Formats;

/// <summary>
/// Drives section tracking, timeline events, and eased parameter transitions
/// from playback position. <see cref="Advance"/> fires events that fall inside
/// the elapsed window; <see cref="Seek"/> deterministically replays every event
/// up to the new position with transitions completed (the WinForms runtime
/// updated lastPosition before computing the window, which silenced
/// zero-duration events — fixed here).
/// </summary>
public sealed class ReactiveRuntime
{
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "theme", "visualizer", "lyrics", "shader",
    };

    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "transition", "reset",
    };

    private ReactiveTimelineDocument? _document;
    private readonly Dictionary<string, object?> _currentParams = [];
    private readonly List<ActiveTransition> _activeTransitions = [];
    private double _lastPosition = -1;

    public event EventHandler<ReactiveSectionChangedEventArgs>? SectionChanged;
    public event EventHandler<ReactiveParamsChangedEventArgs>? ParamsChanged;

    public ReactiveSection? CurrentSection { get; private set; }

    public bool IsLoaded => _document is not null;

    public void Load(ReactiveTimelineDocument? doc)
    {
        _document = doc is { } d && d.IsValid() ? d : null;
        _currentParams.Clear();
        _activeTransitions.Clear();
        CurrentSection = null;
        _lastPosition = -1;
    }

    /// <summary>Continuous playback tick. Fires events in (lastPosition, position].</summary>
    public void Advance(double positionSeconds)
    {
        if (_document is null || positionSeconds <= _lastPosition)
        {
            return;
        }

        var previous = _lastPosition;
        _lastPosition = positionSeconds;

        UpdateSection(positionSeconds);

        foreach (var evt in _document.Timeline)
        {
            if (evt.Time > previous && evt.Time <= positionSeconds)
            {
                ApplyEvent(evt, positionSeconds);
            }
        }

        TickTransitions(positionSeconds);
    }

    /// <summary>
    /// Jump to an arbitrary position: state is rebuilt by replaying all events up
    /// to the target instantly, so seeking backwards or forwards is deterministic.
    /// </summary>
    public void Seek(double positionSeconds)
    {
        if (_document is null)
        {
            return;
        }

        _currentParams.Clear();
        _activeTransitions.Clear();
        _lastPosition = positionSeconds;

        foreach (var evt in _document.Timeline.OrderBy(static evt => evt.Time))
        {
            if (evt.Time > positionSeconds)
            {
                break;
            }

            if (evt.Duration > 0 && evt.Time + evt.Duration > positionSeconds)
            {
                // Mid-transition at the seek target: start it from current state.
                ApplyEvent(evt, evt.Time);
            }
            else
            {
                MergeParams(evt.Params);
            }
        }

        UpdateSection(positionSeconds);
        TickTransitions(positionSeconds);
        ParamsChanged?.Invoke(this, new ReactiveParamsChangedEventArgs(string.Empty, new Dictionary<string, object?>(_currentParams)));
    }

    public ReactiveRuntimeState GetState() =>
        new(
            CurrentSection,
            [.. _activeTransitions.Select(static transition => transition.Event)],
            new Dictionary<string, object?>(_currentParams));

    private void UpdateSection(double pos)
    {
        if (_document is null)
        {
            return;
        }

        ReactiveSection? newSection = null;
        foreach (var section in _document.Sections)
        {
            if (pos >= section.Start && pos < section.End)
            {
                newSection = section;
                break;
            }
        }

        if (newSection?.Id != CurrentSection?.Id)
        {
            CurrentSection = newSection;
            SectionChanged?.Invoke(this, new ReactiveSectionChangedEventArgs(newSection));
        }
    }

    private void ApplyEvent(ReactiveTimelineEvent evt, double pos)
    {
        var parts = evt.Target.Split(':', 2);
        var targetType = parts[0];

        if (!AllowedTargets.Contains(targetType) || !AllowedActions.Contains(evt.Action))
        {
            return;
        }

        if (evt.Duration > 0)
        {
            _activeTransitions.RemoveAll(transition =>
                string.Equals(transition.Event.Target, evt.Target, StringComparison.OrdinalIgnoreCase));
            _activeTransitions.Add(new ActiveTransition(
                evt, evt.Time, evt.Time + evt.Duration, new Dictionary<string, object?>(_currentParams)));
        }
        else
        {
            MergeParams(evt.Params);
            ParamsChanged?.Invoke(this, new ReactiveParamsChangedEventArgs(targetType, evt.Params));
        }
    }

    private void TickTransitions(double pos)
    {
        var changed = false;
        for (var i = _activeTransitions.Count - 1; i >= 0; i--)
        {
            var transition = _activeTransitions[i];
            var progress = transition.End <= transition.Start
                ? 1.0
                : Math.Clamp((pos - transition.Start) / (transition.End - transition.Start), 0, 1);
            var eased = ApplyEasing(progress, transition.Event.Easing);
            var interpolated = Interpolate(transition.FromParams, transition.Event.Params, eased);
            MergeParams(interpolated);
            changed = true;

            if (pos >= transition.End)
            {
                _activeTransitions.RemoveAt(i);
            }
        }

        if (changed)
        {
            ParamsChanged?.Invoke(this, new ReactiveParamsChangedEventArgs(string.Empty, new Dictionary<string, object?>(_currentParams)));
        }
    }

    private void MergeParams(IReadOnlyDictionary<string, object?> incoming)
    {
        foreach (var kv in incoming)
        {
            _currentParams[kv.Key] = kv.Value;
        }
    }

    private static Dictionary<string, object?> Interpolate(
        Dictionary<string, object?> from,
        Dictionary<string, object?> to,
        double t)
    {
        var result = new Dictionary<string, object?>(from);
        foreach (var kv in to)
        {
            if (!from.TryGetValue(kv.Key, out var fromVal))
            {
                result[kv.Key] = kv.Value;
                continue;
            }

            if (TryGetNumber(fromVal, out var fromNumber) && TryGetNumber(kv.Value, out var toNumber))
            {
                result[kv.Key] = fromNumber + ((toNumber - fromNumber) * t);
            }
            else if (t >= 1.0)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    private static bool TryGetNumber(object? value, out double number)
    {
        switch (value)
        {
            case JsonElement { ValueKind: JsonValueKind.Number } element:
                number = element.GetDouble();
                return true;
            case double d:
                number = d;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            default:
                number = 0;
                return false;
        }
    }

    public static double ApplyEasing(double t, string easing) =>
        easing.ToLowerInvariant() switch
        {
            "incubic" => t * t * t,
            "outcubic" => 1 - Math.Pow(1 - t, 3),
            "inoutcubic" => t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2),
            "insine" => 1 - Math.Cos(t * Math.PI / 2),
            "outsine" => Math.Sin(t * Math.PI / 2),
            _ => t,
        };

    private sealed record ActiveTransition(
        ReactiveTimelineEvent Event,
        double Start,
        double End,
        Dictionary<string, object?> FromParams);
}

public sealed class ReactiveSectionChangedEventArgs(ReactiveSection? section) : EventArgs
{
    public ReactiveSection? Section { get; } = section;
}

public sealed class ReactiveParamsChangedEventArgs(string target, IReadOnlyDictionary<string, object?> @params) : EventArgs
{
    public string Target { get; } = target;
    public IReadOnlyDictionary<string, object?> Params { get; } = @params;
}
