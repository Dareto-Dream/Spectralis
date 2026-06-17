using System;
using Spectralis.App.Controls;
using Spectralis.App.Visualizers;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Services
{
    public class VisualizerService : IDisposable
    {
        private readonly VisualizerRegistry _registry;
        private readonly VisualizerCanvas _canvas;
        private string _currentId = "spectrum-bars";
        private bool _disposed;

        public VisualizerService(VisualizerRegistry registry, VisualizerCanvas canvas)
        {
            _registry = registry;
            _canvas = canvas;
        }

        public void Initialize()
        {
            Switch(_currentId);
        }

        public bool Switch(string id)
        {
            if (!_registry.TryCreate(id, out var viz) || viz == null) return false;
            _canvas.SetVisualizer(viz);
            _currentId = id;
            return true;
        }

        public string CurrentId => _currentId;

        public void SetBeatSensitivity(float sensitivity)
        {
            if (_canvas.CurrentVisualizer is SkiaVisualizerBase svb)
                svb.SetBeatSensitivity(sensitivity);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _canvas.Dispose();
        }
    }
}
