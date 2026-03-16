using System;
using System.Threading.Tasks;
using Spectralis.Core.Lyrics;
using Spectralis.Core.Settings;

namespace Spectralis.App.Services
{
    public class LyricsService : IDisposable
    {
        private readonly LyricsLoader _loader = new();
        private readonly LyricsSync _sync = new();
        private readonly LrcSearchIndex _searchIndex = new();
        private LyricsSettings _settings = new();

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
        public LrcFile? CurrentFile { get; private set; }

        public void ApplySettings(LyricsSettings settings) => _settings = settings;

        public async Task LoadForTrackAsync(string audioFilePath, Func<TimeSpan> positionGetter)
        {
            _sync.Unload();
            _searchIndex.Clear();
            CurrentFile = null;

            if (!_settings.Enabled || string.IsNullOrEmpty(audioFilePath)) return;

            var lrc = await _loader.LoadForTrackAsync(audioFilePath);
            if (lrc != null)
            {
                CurrentFile = lrc;
                _searchIndex.Build(lrc);
                _sync.Load(lrc, () => positionGetter() + TimeSpan.FromMilliseconds(_settings.SyncOffsetMs));
            }
        }

        public void LoadInline(string lrcContent, Func<TimeSpan> positionGetter)
        {
            _sync.Unload();
            _searchIndex.Clear();
            CurrentFile = null;

            var lrc = _loader.ParseInline(lrcContent);
            if (lrc != null)
            {
                CurrentFile = lrc;
                _searchIndex.Build(lrc);
                _sync.Load(lrc, positionGetter);
            }
        }

        public IReadOnlyList<(LrcLine Line, int LineIndex)> Search(string query) =>
            _searchIndex.Search(query);

        public void Unload()
        {
            _sync.Unload();
            _searchIndex.Clear();
            CurrentFile = null;
        }

        public void Dispose()
        {
            _sync.Unload();
            _sync.Dispose();
        }
    }
}
