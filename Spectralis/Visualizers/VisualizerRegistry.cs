using System;
using System.Collections.Generic;

namespace Spectralis.Visualizers
{
    public class VisualizerRegistry
    {
        private readonly Dictionary<string, Func<IVisualizer>> _factories =
            new Dictionary<string, Func<IVisualizer>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, Func<IVisualizer>> Factories => _factories;

        public VisualizerRegistry()
        {
            Register("Spectrum Analyzer", () => new SpectrumAnalyzer());
            Register("Waveform", () => new WaveformDisplay());
            Register("Oscilloscope", () => new OscilloscopeVisualizer());
            Register("VU Meter", () => new VuMeterVisualizer());
            Register("Circular Spectrum", () => new CircularSpectrumVisualizer());
        }

        public void Register(string name, Func<IVisualizer> factory)
        {
            _factories[name] = factory;
        }

        public IVisualizer Create(string name)
        {
            if (_factories.TryGetValue(name, out var factory))
                return factory();
            return new SpectrumAnalyzer();
        }

        public IReadOnlyList<string> GetNames()
        {
            var names = new List<string>(_factories.Keys);
            names.Sort();
            return names;
        }
    }
}
