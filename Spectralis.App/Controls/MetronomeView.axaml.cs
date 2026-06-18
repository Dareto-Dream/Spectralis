using System;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace Spectralis.App.Controls
{
    public partial class MetronomeView : UserControl
    {
        public static readonly StyledProperty<float> BpmProperty =
            AvaloniaProperty.Register<MetronomeView, float>(nameof(Bpm));

        public static readonly StyledProperty<string> KeyNameProperty =
            AvaloniaProperty.Register<MetronomeView, string>(nameof(KeyName), string.Empty);

        public float Bpm
        {
            get => GetValue(BpmProperty);
            set => SetValue(BpmProperty, value);
        }

        public string KeyName
        {
            get => GetValue(KeyNameProperty);
            set => SetValue(KeyNameProperty, value);
        }

        private Timer? _timer;
        private bool _isFlash;

        public MetronomeView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            UpdateTimer();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _timer?.Dispose();
            _timer = null;
        }

        static MetronomeView()
        {
            BpmProperty.Changed.AddClassHandler<MetronomeView>((s, _) => s.UpdateTimer());
            KeyNameProperty.Changed.AddClassHandler<MetronomeView>((s, _) => s.UpdateKeyLabel());
        }

        private void UpdateTimer()
        {
            _timer?.Dispose();
            if (Bpm < 40f) return;
            double interval = 60_000.0 / Bpm;
            _timer = new Timer(interval) { AutoReset = true };
            _timer.Elapsed += OnTick;
            _timer.Start();
            UpdateBpmLabel();
        }

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            _isFlash = !_isFlash;
            Dispatcher.UIThread.Post(() =>
            {
                if (this.FindControl<Ellipse>("PulseDot") is { } dot)
                    dot.Fill = _isFlash
                        ? new SolidColorBrush(Color.FromArgb(255, 120, 100, 220))
                        : new SolidColorBrush(Color.FromArgb(255, 58, 58, 106));
            });
        }

        private void UpdateBpmLabel()
        {
            if (this.FindControl<TextBlock>("BpmLabel") is { } label)
                label.Text = Bpm > 0 ? $"{Bpm:F1} BPM" : "-- BPM";
        }

        private void UpdateKeyLabel()
        {
            if (this.FindControl<TextBlock>("KeyLabel") is { } label)
                label.Text = string.IsNullOrEmpty(KeyName) ? "Key: --" : $"Key: {KeyName}";
        }
    }
}
