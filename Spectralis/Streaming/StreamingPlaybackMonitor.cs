using System;
using System.Timers;

namespace Spectralis.Streaming
{
    public class StreamingPlaybackMonitor : IDisposable
    {
        private readonly StreamingHistoryStore _history;
        private readonly System.Timers.Timer _timer;
        private StreamingTrack _currentTrack;
        private DateTime _trackStarted;
        private bool _logged;

        public StreamingPlaybackMonitor(StreamingHistoryStore history)
        {
            _history = history;
            _timer = new System.Timers.Timer(5000);
            _timer.Elapsed += OnTick;
            _timer.AutoReset = true;
        }

        public void OnTrackStarted(StreamingTrack track)
        {
            _currentTrack = track;
            _trackStarted = DateTime.UtcNow;
            _logged = false;
            _timer.Start();
        }

        public void OnTrackStopped()
        {
            _timer.Stop();
            _currentTrack = null;
        }

        private void OnTick(object sender, ElapsedEventArgs e)
        {
            if (_currentTrack == null || _logged) return;

            double elapsed = (DateTime.UtcNow - _trackStarted).TotalSeconds;
            double threshold = Math.Min(30, _currentTrack.Duration.TotalSeconds * 0.5);

            if (elapsed >= threshold)
            {
                _history.Add(_currentTrack);
                _logged = true;
                _timer.Stop();
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
