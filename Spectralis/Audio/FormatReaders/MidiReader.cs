using System;
using System.IO;
using NAudio.Midi;
using NAudio.Wave;

namespace Spectralis.Audio.FormatReaders
{
    public class MidiReader : IAudioReader
    {
        private readonly MidiFile _midiFile;
        private readonly WaveFormat _waveFormat;
        private readonly int _totalTicks;
        private int _currentTick;

        public MidiReader(string filePath)
        {
            _midiFile = new MidiFile(filePath, false);
            _waveFormat = new WaveFormat(44100, 16, 2);
            _totalTicks = (int)(_midiFile.Events[0][_midiFile.Events[0].Count - 1].AbsoluteTime);
            Duration = EstimateDuration();
        }

        public WaveFormat WaveFormat => _waveFormat;
        public TimeSpan Duration { get; }
        public string SupportedExtension => ".mid";

        public TimeSpan Position
        {
            get => TimeSpan.FromTicks((long)((double)_currentTick / _totalTicks * Duration.Ticks));
            set => _currentTick = (int)((value.Ticks / (double)Duration.Ticks) * _totalTicks);
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            _currentTick += (int)((double)count / _waveFormat.AverageBytesPerSecond * _midiFile.DeltaTicksPerQuarterNote);
            return count;
        }

        private TimeSpan EstimateDuration()
        {
            double tempo = 500000;
            double seconds = 0;

            foreach (var trackEvents in _midiFile.Events)
            {
                foreach (var evt in trackEvents)
                {
                    if (evt is TempoEvent te)
                        tempo = te.MicrosecondsPerQuarterNote;
                }
            }

            seconds = (_totalTicks / (double)_midiFile.DeltaTicksPerQuarterNote) * (tempo / 1_000_000.0);
            return TimeSpan.FromSeconds(seconds);
        }

        public void Dispose() { }
    }
}
