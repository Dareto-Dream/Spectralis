using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Styling;

namespace Spectralis.App.Services;

/// <summary>One switch for interface motion. Audio-reactive rendering has its own controls.</summary>
public sealed class AppMotionService : AvaloniaObject
{
    private AppMotionService() { }
    public static readonly AttachedProperty<bool> RevealOnShowProperty =
        AvaloniaProperty.RegisterAttached<AppMotionService, Control, bool>("RevealOnShow");
    public static bool GetRevealOnShow(Control control) => control.GetValue(RevealOnShowProperty);
    public static void SetRevealOnShow(Control control, bool value) => control.SetValue(RevealOnShowProperty, value);
    static AppMotionService()
    {
        Control.IsVisibleProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.IsVisible && GetRevealOnShow(control)) Reveal(control);
        });
    }

    private static readonly Dictionary<Control, CancellationTokenSource> Active = new();
    public static bool Enabled { get; private set; } = true;

    public static void Apply(bool enabled)
    {
        Enabled = enabled;
        if (!enabled)
            foreach (var token in Active.Values.ToArray()) token.Cancel();
        if (Application.Current is not { } app) return;
        app.Resources["Duration.Fast"] = enabled ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero;
        app.Resources["Duration.Base"] = enabled ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
        app.Resources["Motion.Expansion"] = enabled ? new CrossFade(TimeSpan.FromMilliseconds(150)) : null;
    }

    public static async void Reveal(Control control)
    {
        if (Active.TryGetValue(control, out var previous)) previous.Cancel();
        if (!Enabled) return;
        using var cancellation = new CancellationTokenSource();
        Active[control] = cancellation;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(180),
            Easing = new CubicEaseOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, .72d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 1d) } },
            },
        };
        try { await animation.RunAsync(control, cancellation.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            if (Active.TryGetValue(control, out var current) && current == cancellation) Active.Remove(control);
        }
    }
}
