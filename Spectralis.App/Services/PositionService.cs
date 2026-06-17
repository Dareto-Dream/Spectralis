using System;
using System.Timers;
using Spectralis.Core.Audio;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Services
{
    public class PositionService : IDisposable
    {
        private readonly IAudioEngine _engine;
        private readonly PlayerViewModel _vm;
        private readonly Timer _timer;
        private bool _disposed;

        public PositionService(IAudioEngine engine, PlayerViewModel vm)
        {
            _engine = engine;
            _vm = vm;
            _timer = new Timer(250);
            _timer.Elapsed += OnTick;
            _timer.AutoReset = true;
            _timer.Start();
        }

        private void OnTick(object? sender, ElapsedEventArgs e)
        {
            try
            {
                _vm.Position = _engine.Position;
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
