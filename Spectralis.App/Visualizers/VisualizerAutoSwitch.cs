using System;
using System.Collections.Generic;
using System.Timers;
using Spectralis.App.Services;

namespace Spectralis.App.Visualizers
{
    public class VisualizerAutoSwitch : IDisposable
    {
        private readonly VisualizerService _service;
        private readonly List<string> _cycle;
        private readonly Timer _timer;
        private int _index;
        private bool _disposed;

        public bool IsEnabled { get; private set; }

        public VisualizerAutoSwitch(VisualizerService service, IEnumerable<string> cycle, double intervalSeconds = 60.0)
        {
            _service = service;
            _cycle = new List<string>(cycle);
            _timer = new Timer(intervalSeconds * 1000);
            _timer.Elapsed += OnTick;
            _timer.AutoReset = true;
        }

        public void Start()
        {
            if (_cycle.Count == 0) return;
            IsEnabled = true;
            _timer.Start();
        }

        public void Stop()
        {
            IsEnabled = false;
            _timer.Stop();
        }

        public void SetInterval(double seconds)
        {
            _timer.Interval = seconds * 1000;
        }

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            if (_cycle.Count == 0) return;
            _index = (_index + 1) % _cycle.Count;
            _service.Switch(_cycle[_index]);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
        }
    }
}
