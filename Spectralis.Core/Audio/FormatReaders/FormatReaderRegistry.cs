using System;
using System.Collections.Generic;
using System.IO;

namespace Spectralis.Core.Audio.FormatReaders
{
    public class FormatReaderRegistry
    {
        private readonly Dictionary<string, Func<string, IAudioReader>> _factories =
            new Dictionary<string, Func<string, IAudioReader>>(StringComparer.OrdinalIgnoreCase);

        public void Register(string extension, Func<string, IAudioReader> factory)
        {
            _factories[extension] = factory;
        }

        public IAudioReader? Create(string filePath)
        {
            string ext = Path.GetExtension(filePath);
            return _factories.TryGetValue(ext, out var factory) ? factory(filePath) : null;
        }

        public bool CanRead(string filePath) =>
            _factories.ContainsKey(Path.GetExtension(filePath));

        public IEnumerable<string> SupportedExtensions => _factories.Keys;

        public static FormatReaderRegistry CreateDefault()
        {
            var r = new FormatReaderRegistry();
            r.Register(".mp3", p => new NaudioFileReader(p));
            r.Register(".wav", p => new NaudioFileReader(p));
            r.Register(".flac", p => new NaudioFileReader(p));
            r.Register(".ogg", p => new NaudioFileReader(p));
            r.Register(".aac", p => new NaudioFileReader(p));
            r.Register(".m4a", p => new NaudioFileReader(p));
            r.Register(".opus", p => new NaudioFileReader(p));
            r.Register(".wma", p => new NaudioFileReader(p));
            return r;
        }
    }
}
