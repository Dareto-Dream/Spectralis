using System;
using System.Timers;

namespace Spectralis.Core.Lyrics
{
    public class LyricsSync : IDisposable
    {
        private LrcFile? _current;
        private int _lastLineIndex = -1;
        private readonly Timer _timer;
        private Func<TimeSpan>? _positionGetter;

        public event EventHandler<LrcLine>? LineChanged;
        public event EventHandler<EnhancedWord>? WordHighlighted;

        public LrcLine? CurrentLine { get; private set; }
        public int CurrentLineIndex => _lastLineIndex;
        public bool IsActive => _current != null;

        public LyricsSync()
        {
            _timer = new Timer(50);
            _timer.Elapsed += OnTimerTick;
        }

        public void Load(LrcFile file, Func<TimeSpan> positionGetter)
        {
            _current = file;
            _positionGetter = positionGetter;
            _lastLineIndex = -1;
            CurrentLine = null;
            _timer.Start();
        }

        public void Unload()
        {
            _timer.Stop();
            _current = null;
            _positionGetter = null;
            _lastLineIndex = -1;
            CurrentLine = null;
        }

        private void OnTimerTick(object? sender, ElapsedEventArgs e)
        {
            if (_current == null || _positionGetter == null) return;
            var pos = _positionGetter();
            int idx = _current.GetCurrentLineIndex(pos);

            if (idx != _lastLineIndex && idx >= 0)
            {
                _lastLineIndex = idx;
                CurrentLine = _current.Lines[idx];
                LineChanged?.Invoke(this, CurrentLine);

                if (CurrentLine.IsEnhanced)
                    HighlightCurrentWord(pos);
            }
            else if (idx >= 0 && CurrentLine?.IsEnhanced == true)
            {
                HighlightCurrentWord(pos);
            }
        }

        private void HighlightCurrentWord(TimeSpan pos)
        {
            if (CurrentLine?.Words == null) return;
            var adjusted = pos - (_current?.Offset ?? TimeSpan.Zero);
            EnhancedWord? active = null;
            foreach (var word in CurrentLine.Words)
            {
                if (word.Timestamp <= adjusted) active = word;
                else break;
            }
            if (active != null) WordHighlighted?.Invoke(this, active);
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
        }
    }
}
