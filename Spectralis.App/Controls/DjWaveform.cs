using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Controls;

public sealed class DjWaveform : Control
{
    public static readonly StyledProperty<float[]?> SamplesProperty = AvaloniaProperty.Register<DjWaveform, float[]?>(nameof(Samples));
    public static readonly StyledProperty<double> PositionProperty = AvaloniaProperty.Register<DjWaveform, double>(nameof(Position));
    public static readonly StyledProperty<double> DurationProperty = AvaloniaProperty.Register<DjWaveform, double>(nameof(Duration));
    public static readonly StyledProperty<double> BpmProperty = AvaloniaProperty.Register<DjWaveform, double>(nameof(Bpm));
    public static readonly StyledProperty<double> BeatOffsetProperty = AvaloniaProperty.Register<DjWaveform, double>(nameof(BeatOffset));
    public static readonly StyledProperty<IBrush?> SignalProperty = AvaloniaProperty.Register<DjWaveform, IBrush?>(nameof(Signal));
    public static readonly StyledProperty<double?> LoopStartProperty = AvaloniaProperty.Register<DjWaveform, double?>(nameof(LoopStart));
    public static readonly StyledProperty<double?> LoopEndProperty = AvaloniaProperty.Register<DjWaveform, double?>(nameof(LoopEnd));
    public float[]? Samples { get => GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public double Position { get => GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public double Duration { get => GetValue(DurationProperty); set => SetValue(DurationProperty, value); }
    public double Bpm { get => GetValue(BpmProperty); set => SetValue(BpmProperty, value); }
    public double BeatOffset { get => GetValue(BeatOffsetProperty); set => SetValue(BeatOffsetProperty, value); }
    public IBrush? Signal { get => GetValue(SignalProperty); set => SetValue(SignalProperty, value); }
    public double? LoopStart { get => GetValue(LoopStartProperty); set => SetValue(LoopStartProperty, value); }
    public double? LoopEnd { get => GetValue(LoopEndProperty); set => SetValue(LoopEndProperty, value); }
    static DjWaveform() => AffectsRender<DjWaveform>(SamplesProperty, PositionProperty, DurationProperty, BpmProperty,
        BeatOffsetProperty, SignalProperty, LoopStartProperty, LoopEndProperty);

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Samples is not { Length: > 0 } samples || Duration <= 0) return;
        var width = Bounds.Width; var height = Bounds.Height;
        var signal = Signal ?? Brushes.Orange;
        if (LoopStart is { } start && LoopEnd is { } end)
            using (context.PushOpacity(.16)) context.FillRectangle(signal, new Rect(start / Duration * width, 0, (end - start) / Duration * width, height));
        var played = Position / Duration * width;
        var dim = new Pen(signal, 1);
        for (var x = 0; x < width; x += 2)
        {
            var first = (int)(x / width * samples.Length);
            var last = Math.Min(samples.Length - 1, (int)((x + 2) / width * samples.Length));
            var amplitude = 0f;
            for (var j = first; j <= last; j++) amplitude = Math.Max(amplitude, samples[j]);
            var y = Math.Min(1, amplitude) * (height * .43);
            using (context.PushOpacity(x < played ? .95 : .38))
                context.DrawLine(dim, new Point(x, height / 2 - y), new Point(x, height / 2 + y));
        }
        if (Bpm > 0)
        {
            var beat = 60 / Bpm;
            var stride = Math.Max(1, (int)Math.Ceiling(Duration / beat / Math.Max(1, width / 6)));
            for (var t = BeatOffset; t < Duration; t += beat * stride)
                context.DrawLine(dim, new Point(t / Duration * width, height - 5), new Point(t / Duration * width, height));
        }
        context.DrawLine(new Pen(signal, 2), new Point(played, 0), new Point(played, height));
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || DataContext is not DjDeckViewModel deck) return;
        deck.Position = Math.Clamp(e.GetPosition(this).X / Math.Max(1, Bounds.Width), 0, 1) * Duration;
        e.Handled = true;
    }
}
