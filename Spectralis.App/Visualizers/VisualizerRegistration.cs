using Spectralis.Core.Visualizers;

namespace Spectralis.App.Visualizers
{
    public static class VisualizerRegistration
    {
        public static void RegisterAll(VisualizerRegistry registry)
        {
            registry.Register(new VisualizerInfo { Id = "spectrum-bars", DisplayName = "Spectrum Bars", Category = "Spectrum", Factory = () => new SpectrumBarsVisualizer() });
            registry.Register(new VisualizerInfo { Id = "waveform", DisplayName = "Waveform", Category = "Waveform", Factory = () => new WaveformVisualizer() });
            registry.Register(new VisualizerInfo { Id = "oscilloscope", DisplayName = "Oscilloscope", Category = "Waveform", Factory = () => new OscilloscopeVisualizer() });
            registry.Register(new VisualizerInfo { Id = "vu-meter", DisplayName = "VU Meter", Category = "Level", Factory = () => new VuMeterVisualizer() });
            registry.Register(new VisualizerInfo { Id = "circular-spectrum", DisplayName = "Circular Spectrum", Category = "Spectrum", Factory = () => new CircularSpectrumVisualizer() });
            registry.Register(new VisualizerInfo { Id = "mirror", DisplayName = "Mirror", Category = "Spectrum", Factory = () => new MirrorVisualizer() });
            registry.Register(new VisualizerInfo { Id = "heatmap", DisplayName = "Heatmap", Category = "Spectrum", Factory = () => new HeatmapVisualizer() });
            registry.Register(new VisualizerInfo { Id = "particles", DisplayName = "Particles", Category = "Particles", Factory = () => new ParticleSystemVisualizer() });
            registry.Register(new VisualizerInfo { Id = "starfield", DisplayName = "Starfield", Category = "Particles", Factory = () => new StarfieldVisualizer() });
            registry.Register(new VisualizerInfo { Id = "lissajous", DisplayName = "Lissajous", Category = "Waveform", Factory = () => new LissajousVisualizer() });
            registry.Register(new VisualizerInfo { Id = "bars-3d", DisplayName = "3D Bars", Category = "Spectrum", IsHardwareAccelerated = true, Factory = () => new Bar3DVisualizer() });
            registry.Register(new VisualizerInfo { Id = "neon-rings", DisplayName = "Neon Rings", Category = "Spectrum", Factory = () => new NeonRingsVisualizer() });
            registry.Register(new VisualizerInfo { Id = "beat-pulse", DisplayName = "Beat Pulse", Category = "Beat", Factory = () => new BeatPulseVisualizer() });
            registry.Register(new VisualizerInfo { Id = "reactive-bg", DisplayName = "Reactive Background", Category = "Ambient", Factory = () => new ReactiveBackgroundVisualizer() });
            registry.Register(new VisualizerInfo { Id = "waterfall", DisplayName = "Waterfall", Category = "Spectrum", Factory = () => new WaterfallVisualizer() });
            registry.Register(new VisualizerInfo { Id = "spiral", DisplayName = "Spiral", Category = "Spectrum", Factory = () => new SpiralSpectrumVisualizer() });
            registry.Register(new VisualizerInfo { Id = "chromatic", DisplayName = "Chromatic", Category = "Spectrum", Factory = () => new ChromaticVisualizer() });
            registry.Register(new VisualizerInfo { Id = "mosaic", DisplayName = "Mosaic", Category = "Spectrum", Factory = () => new MosaicVisualizer() });
            registry.Register(new VisualizerInfo { Id = "reactive-gradient", DisplayName = "Reactive Gradient", Category = "Ambient", Factory = () => new AudioReactiveGradientVisualizer() });
        }
    }
}
