using System;
using System.Windows.Forms;

namespace Spectralis.Audio
{
    public class PlaybackPositionTimer : IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly Timer _timer;
        private bool _disposed;

        public event EventHandler<(TimeSpan Position, TimeSpan Duration)> PositionTicked;

        public PlaybackPositionTimer(AudioEngine engine, int intervalMs = 500)
        {
            _engine = engine;
            _timer = new Timer { Interval = intervalMs };
            _timer.Tick += OnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void OnTick(object sender, EventArgs e)
        {
            try
            {
                var pos = _engine.Position;
                var dur = _engine.Duration;
                PositionTicked?.Invoke(this, (pos, dur));
            }
            catch { }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
