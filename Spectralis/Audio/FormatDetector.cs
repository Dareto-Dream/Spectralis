using System;
using System.Collections.Generic;
using System.IO;
using Spectralis.Audio.FormatReaders;

namespace Spectralis.Audio
{
    public static class FormatDetector
    {
        private static readonly Dictionary<string, Func<string, IAudioReader>> _readers =
            new Dictionary<string, Func<string, IAudioReader>>(StringComparer.OrdinalIgnoreCase)
            {
                { ".mp3",  path => new Mp3Reader(path) },
                { ".wav",  path => new WavReader(path) },
                { ".flac", path => new FlacReader(path) },
                { ".ogg",  path => new OggReader(path) },
                { ".opus", path => new OpusReader(path) },
                { ".m4a",  path => new M4aReader(path) },
                { ".aac",  path => new AacReader(path) },
                { ".mid",  path => new MidiReader(path) },
                { ".midi", path => new MidiReader(path) },
            };

        public static bool IsSupported(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            return _readers.ContainsKey(ext);
        }

        public static IAudioReader CreateReader(string filePath)
        {
            var ext = Path.GetExtension(filePath);
            if (!_readers.TryGetValue(ext, out var factory))
                throw new NotSupportedException($"Unsupported audio format: {ext}");
            return factory(filePath);
        }

        public static IEnumerable<string> SupportedExtensions => _readers.Keys;

        public static string GetOpenFileFilter()
        {
            return "Audio Files|*.mp3;*.wav;*.flac;*.ogg;*.opus;*.m4a;*.aac;*.mid;*.midi|All Files|*.*";
        }
    }
}
