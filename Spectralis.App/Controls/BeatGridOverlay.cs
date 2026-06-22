using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Spectralis.App.Controls;

/// <summary>Thin strip of beat ticks rendered under the scrubber when BPM is known.</summary>
public sealed class BeatGridOverlay : Control
{
    public static readonly StyledProperty<double> BpmProperty =
        AvaloniaProperty.Register<BeatGridOverlay, double>(nameof(Bpm));

    public static readonly StyledProperty<double> FirstBeatSecondsProperty =
        AvaloniaProperty.Register<BeatGridOverlay, double>(nameof(FirstBeatSeconds));

    public static readonly StyledProperty<double> DurationSecondsProperty =
        AvaloniaProperty.Register<BeatGridOverlay, double>(nameof(DurationSeconds));

    public static readonly StyledProperty<IBrush?> TickBrushProperty =
        AvaloniaProperty.Register<BeatGridOverlay, IBrush?>(nameof(TickBrush));

    static BeatGridOverlay()
    {
        AffectsRender<BeatGridOverlay>(BpmProperty, FirstBeatSecondsProperty, DurationSecondsProperty, TickBrushProperty);
    }

    public double Bpm
    {
        get => GetValue(BpmProperty);
        set => SetValue(BpmProperty, value);
    }

    public double FirstBeatSeconds
    {
        get => GetValue(FirstBeatSecondsProperty);
        set => SetValue(FirstBeatSecondsProperty, value);
    }

    public double DurationSeconds
    {
        get => GetValue(DurationSecondsProperty);
        set => SetValue(DurationSecondsProperty, value);
    }

    public IBrush? TickBrush
    {
        get => GetValue(TickBrushProperty);
        set => SetValue(TickBrushProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var bpm = Bpm;
        var duration = DurationSeconds;
        if (bpm <= 0 || duration <= 0 || Bounds.Width <= 0)
        {
            return;
        }

        var brush = TickBrush ?? new SolidColorBrush(Color.FromArgb(160, 255, 200, 80));
        var pen = new Pen(brush, 1);
        var beatPeriod = 60.0 / bpm;
        var width = Bounds.Width;
        var height = Bounds.Height;

        // Cap the tick count so absurd BPM/duration combinations stay cheap.
        var maxTicks = (int)Math.Min(4000, (duration / beatPeriod) + 1);
        var t = Math.Max(0, FirstBeatSeconds);
        for (var i = 0; i < maxTicks && t <= duration; i++, t += beatPeriod)
        {
            var x = t / duration * width;
            context.DrawLine(pen, new Point(x, 0), new Point(x, height));
        }
    }
}
