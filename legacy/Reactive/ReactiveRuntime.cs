using System.Text.Json;

namespace Spectralis;

internal sealed class ReactiveRuntime
{
    private static readonly HashSet<string> AllowedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "theme", "visualizer", "lyrics", "shader"
    };

    private static readonly HashSet<string> AllowedActions = new(StringComparer.OrdinalIgnoreCase)
    {
        "set", "transition", "reset"
    };

    private ReactiveTimelineDocument? document;
    private readonly Dictionary<string, object?> currentParams = [];
    private readonly List<ActiveTransition> activeTransitions = [];
    private double lastPosition = -1;

    public event EventHandler<ReactiveSectionChangedEventArgs>? SectionChanged;
    public event EventHandler<ReactiveParamsChangedEventArgs>? ParamsChanged;

    public ReactiveSection? CurrentSection { get; private set; }

    public void Load(ReactiveTimelineDocument? doc)
    {
        document = doc is { } d && d.IsValid() ? d : null;
        currentParams.Clear();
        activeTransitions.Clear();
        CurrentSection = null;
        lastPosition = -1;
    }

    public void Seek(double positionSeconds)
    {
        if (document is null)
            return;

        lastPosition = positionSeconds;
        UpdateSection(positionSeconds);
        FireElapsedEvents(positionSeconds);
    }

    public void Advance(double positionSeconds)
    {
        if (document is null || positionSeconds <= lastPosition)
            return;

        Seek(positionSeconds);
        TickTransitions(positionSeconds);
    }

    public ReactiveRuntimeState GetState() =>
        new(CurrentSection, [..activeTransitions.Select(t => t.Event)], new Dictionary<string, object?>(currentParams));

    private void UpdateSection(double pos)
    {
        if (document is null)
            return;

        ReactiveSection? newSection = null;
        foreach (var section in document.Sections)
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

    private void FireElapsedEvents(double pos)
    {
        if (document is null)
            return;

        var prev = lastPosition;
        foreach (var evt in document.Timeline)
        {
            if (evt.Time > prev && evt.Time <= pos)
                ApplyEvent(evt, pos);
        }
    }

    private void ApplyEvent(ReactiveTimelineEvent evt, double pos)
    {
        var parts = evt.Target.Split(':', 2);
        var targetType = parts[0];

        if (!AllowedTargets.Contains(targetType))
            return;

        if (!AllowedActions.Contains(evt.Action))
            return;

        if (evt.Duration > 0)
        {
            activeTransitions.RemoveAll(t => string.Equals(t.Event.Target, evt.Target, StringComparison.OrdinalIgnoreCase));
            activeTransitions.Add(new ActiveTransition(evt, pos, pos + evt.Duration, currentParams.ToDictionary()));
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
        for (var i = activeTransitions.Count - 1; i >= 0; i--)
        {
            var t = activeTransitions[i];
            var progress = t.End <= t.Start ? 1.0 : Math.Clamp((pos - t.Start) / (t.End - t.Start), 0, 1);
            var eased = ApplyEasing(progress, t.Event.Easing);
            var interpolated = Interpolate(t.FromParams, t.Event.Params, eased);
            MergeParams(interpolated);
            changed = true;

            if (pos >= t.End)
                activeTransitions.RemoveAt(i);
        }

        if (changed)
            ParamsChanged?.Invoke(this, new ReactiveParamsChangedEventArgs("", currentParams));
    }

    private void MergeParams(IReadOnlyDictionary<string, object?> incoming)
    {
        foreach (var kv in incoming)
            currentParams[kv.Key] = kv.Value;
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

            if (fromVal is JsonElement fj && kv.Value is JsonElement tj)
            {
                if (fj.ValueKind == JsonValueKind.Number && tj.ValueKind == JsonValueKind.Number)
                    result[kv.Key] = fj.GetDouble() + (tj.GetDouble() - fj.GetDouble()) * t;
                else if (t >= 1.0)
                    result[kv.Key] = kv.Value;
            }
            else if (t >= 1.0)
            {
                result[kv.Key] = kv.Value;
            }
        }

        return result;
    }

    private static double ApplyEasing(double t, string easing) =>
        easing.ToLowerInvariant() switch
        {
            "incubic"   => t * t * t,
            "outcubic"  => 1 - Math.Pow(1 - t, 3),
            "inoutcubic" => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2,
            "insine"    => 1 - Math.Cos(t * Math.PI / 2),
            "outsine"   => Math.Sin(t * Math.PI / 2),
            _ => t
        };

    private sealed record ActiveTransition(
        ReactiveTimelineEvent Event,
        double Start,
        double End,
        Dictionary<string, object?> FromParams);
}

internal sealed class ReactiveSectionChangedEventArgs(ReactiveSection? section) : EventArgs
{
    public ReactiveSection? Section { get; } = section;
}

internal sealed class ReactiveParamsChangedEventArgs(string target, IReadOnlyDictionary<string, object?> @params) : EventArgs
{
    public string Target { get; } = target;
    public IReadOnlyDictionary<string, object?> Params { get; } = @params;
}
