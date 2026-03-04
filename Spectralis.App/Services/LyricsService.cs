using System;
using System.Threading.Tasks;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.Services
{
    public class LyricsService : IDisposable
    {
        private readonly LyricsLoader _loader = new();
        private readonly LyricsSync _sync = new();

        public event EventHandler<LrcLine>? LineChanged
        {
            add => _sync.LineChanged += value;
            remove => _sync.LineChanged -= value;
        }

        public event EventHandler<EnhancedWord>? WordHighlighted
        {
            add => _sync.WordHighlighted += value;
            remove => _sync.WordHighlighted -= value;
        }

        public LrcLine? CurrentLine => _sync.CurrentLine;
        public bool IsActive => _sync.IsActive;

        public async Task LoadForTrackAsync(string audioFilePath, Func<TimeSpan> positionGetter)
        {
            _sync.Unload();
            if (string.IsNullOrEmpty(audioFilePath)) return;

            var lrc = await _loader.LoadForTrackAsync(audioFilePath);
            if (lrc != null)
                _sync.Load(lrc, positionGetter);
        }

        public void LoadInline(string lrcContent, Func<TimeSpan> positionGetter)
        {
            _sync.Unload();
            var lrc = _loader.ParseInline(lrcContent);
            if (lrc != null)
                _sync.Load(lrc, positionGetter);
        }

        public void Unload() => _sync.Unload();

        public void Dispose()
        {
            _sync.Unload();
            _sync.Dispose();
        }
    }
}
