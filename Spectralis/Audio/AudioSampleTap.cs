using System;
using NAudio.Wave;

namespace Spectralis.Audio
{
    public class AudioSampleTap : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private FftProcessor _fft;
        private WaveformBuffer _waveform;

        public WaveFormat WaveFormat => _source.WaveFormat;

        public event EventHandler SamplesReady;

        public AudioSampleTap(ISampleProvider source, FftProcessor fft, WaveformBuffer waveform)
        {
            _source = source;
            _fft = fft;
            _waveform = waveform;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);

            if (read > 0)
            {
                var mono = DownmixToMono(buffer, offset, read, WaveFormat.Channels);
                _fft.Push(mono, 0, mono.Length);
                _waveform.Push(mono, 0, mono.Length);
                SamplesReady?.Invoke(this, EventArgs.Empty);
            }

            return read;
        }

        private static float[] DownmixToMono(float[] buffer, int offset, int count, int channels)
        {
            if (channels == 1)
            {
                var m = new float[count];
                Array.Copy(buffer, offset, m, 0, count);
                return m;
            }

            int frames = count / channels;
            var mono = new float[frames];
            for (int i = 0; i < frames; i++)
            {
                float sum = 0;
                for (int ch = 0; ch < channels; ch++)
                    sum += buffer[offset + i * channels + ch];
                mono[i] = sum / channels;
            }
            return mono;
        }
    }
}
