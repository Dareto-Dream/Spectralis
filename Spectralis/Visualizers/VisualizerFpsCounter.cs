using System;
using System.Diagnostics;

namespace Spectralis.Visualizers
{
    public class VisualizerFpsCounter
    {
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private int _frames;
        private double _fps;
        private long _lastTick;

        public double Fps => _fps;

        public void Tick()
        {
            _frames++;
            long now = _sw.ElapsedMilliseconds;
            long delta = now - _lastTick;

            if (delta >= 1000)
            {
                _fps = _frames * 1000.0 / delta;
                _frames = 0;
                _lastTick = now;
            }
        }

        public void Reset()
        {
            _frames = 0;
            _fps = 0;
            _sw.Restart();
            _lastTick = 0;
        }
    }
}
