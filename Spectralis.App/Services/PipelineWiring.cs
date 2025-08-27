using Spectralis.App.Controls;
using Spectralis.Core.Audio;

namespace Spectralis.App.Services
{
    public static class PipelineWiring
    {
        public static void Attach(AudioPipeline pipeline, SpectrumBarsControl spectrum, WaveformView waveform)
        {
            spectrum.Pipeline = pipeline;
            waveform.Pipeline = pipeline;
        }

        public static void Detach(AudioPipeline pipeline, SpectrumBarsControl spectrum, WaveformView waveform)
        {
            spectrum.Pipeline = null;
            waveform.Pipeline = null;
        }

        public static void AttachTap(IAudioEngine engine, AudioPipeline pipeline)
        {
            var tap = engine.CreateTap();
            if (tap != null) pipeline.Attach(tap);
        }
    }
}
