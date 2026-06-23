using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using NAudio.Wave;

namespace Spectralis.App.Views;

/// <summary>Topmost metronome: BPM spinner, audio click, beat flash, tap tempo.</summary>
public partial class MetronomeWindow : Window
{
    private DispatcherTimer? _timer;
    private DispatcherTimer? _flashTimer;
    private bool _running;
    private DateTime _lastTapTime = DateTime.MinValue;
    private readonly List<double> _tapIntervals = [];
    private static readonly byte[] ClickBytes = BuildClickBytes();

    public MetronomeWindow()
        : this(120f)
    {
    }

    public MetronomeWindow(float initialBpm)
    {
        InitializeComponent();
        BpmBox.Value = (decimal)Math.Clamp(initialBpm, 40f, 240f);
        Closing += (_, _) => Stop();
    }

    private double Bpm => (double)(BpmBox.Value ?? 120);

    private void OnBpmChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        if (_running)
        {
            RestartTimer();
        }
    }

    private void OnToggle(object? sender, RoutedEventArgs e)
    {
        if (_running)
        {
            Stop();
        }
        else
        {
            Start();
        }
    }

    private void Start()
    {
        _running = true;
        ToggleButton.Content = "■ Stop";
        RestartTimer();
    }

    private void Stop()
    {
        _running = false;
        ToggleButton.Content = "▶ Start";
        _timer?.Stop();
        _timer = null;
    }

    private void RestartTimer()
    {
        _timer?.Stop();
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(60_000.0 / Math.Max(40, Bpm)),
        };
        _timer.Tick += (_, _) => OnBeat();
        _timer.Start();
        OnBeat();
    }

    private void OnBeat()
    {
        if (AudioCheck.IsChecked == true)
        {
            PlayClick();
        }

        FlashBeat();
    }

    private void FlashBeat()
    {
        if (this.TryFindResource("Brush.Signal", ActualThemeVariant, out var accent) && accent is IBrush accentBrush)
        {
            FlashPanel.Background = accentBrush;
        }
        else
        {
            FlashPanel.Background = new SolidColorBrush(Color.FromRgb(244, 152, 82));
        }

        _flashTimer?.Stop();
        _flashTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashTimer?.Stop();
            if (this.TryFindResource("Brush.Bg.Raised", ActualThemeVariant, out var raised) && raised is IBrush raisedBrush)
            {
                FlashPanel.Background = raisedBrush;
            }
        };
        _flashTimer.Start();
    }

    private void OnTap(object? sender, RoutedEventArgs e)
    {
        var now = DateTime.UtcNow;
        if (_lastTapTime != DateTime.MinValue)
        {
            var interval = (now - _lastTapTime).TotalMilliseconds;
            if (interval < 3000)
            {
                _tapIntervals.Add(interval);
                if (_tapIntervals.Count > 8)
                {
                    _tapIntervals.RemoveAt(0);
                }

                var bpm = Math.Clamp(Math.Round(60_000.0 / _tapIntervals.Average(), 1), 40, 240);
                BpmBox.Value = (decimal)bpm;
            }
            else
            {
                _tapIntervals.Clear();
            }
        }

        _lastTapTime = now;
    }

    private static void PlayClick()
    {
        try
        {
            // 30ms 880Hz decaying sine, one shot per beat; output disposes itself.
            var provider = new RawSourceWaveStream(
                new MemoryStream(ClickBytes),
                WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
            var output = new WaveOutEvent { DesiredLatency = 100 };
            output.Init(provider);
            output.Play();
            output.PlaybackStopped += (_, _) =>
            {
                output.Dispose();
                provider.Dispose();
            };
        }
        catch
        {
        }
    }

    private static byte[] BuildClickBytes()
    {
        const int sr = 44100;
        const int count = sr / 33;  // ~30ms
        var samples = new float[count * 2];
        for (var i = 0; i < count; i++)
        {
            var s = (float)(0.7 * Math.Sin(2 * Math.PI * 880.0 * i / sr) * Math.Exp(-i / (sr / 100.0)));
            samples[i * 2] = s;
            samples[(i * 2) + 1] = s;
        }

        var bytes = new byte[samples.Length * 4];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }
}
