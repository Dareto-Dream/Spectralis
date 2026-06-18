using System;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LyricsSyncTests : IDisposable
    {
        private readonly LyricsSync _sync = new();
        private readonly LrcParser _parser = new();

        [Fact]
        public void Load_SetsIsActive()
        {
            var lrc = _parser.Parse("[00:01.00]Line");
            _sync.Load(lrc, () => TimeSpan.Zero);
            Assert.True(_sync.IsActive);
        }

        [Fact]
        public void Unload_ClearsState()
        {
            var lrc = _parser.Parse("[00:01.00]Line");
            _sync.Load(lrc, () => TimeSpan.Zero);
            _sync.Unload();
            Assert.False(_sync.IsActive);
            Assert.Null(_sync.CurrentLine);
        }

        [Fact]
        public async Task LineChanged_FiresWhenPositionPassesTimestamp()
        {
            var lrc = _parser.Parse("[00:00.10]Early\n[00:00.50]Late");
            LrcLine? received = null;
            _sync.LineChanged += (_, line) => received = line;

            var pos = TimeSpan.Zero;
            _sync.Load(lrc, () => pos);

            pos = TimeSpan.FromSeconds(0.2);
            await Task.Delay(200);

            Assert.NotNull(received);
            Assert.Equal("Early", received!.Text);
        }

        [Fact]
        public async Task CurrentLine_UpdatesOverTime()
        {
            var lrc = _parser.Parse("[00:00.05]A\n[00:00.20]B");
            var pos = TimeSpan.FromSeconds(0.06);
            _sync.Load(lrc, () => pos);
            await Task.Delay(150);

            pos = TimeSpan.FromSeconds(0.25);
            await Task.Delay(150);

            Assert.Equal("B", _sync.CurrentLine?.Text);
        }

        [Fact]
        public void CurrentLineIndex_IsNegativeWhenBeforeFirstLine()
        {
            var lrc = _parser.Parse("[00:01.00]Line");
            _sync.Load(lrc, () => TimeSpan.Zero);
            Assert.Equal(-1, _sync.CurrentLineIndex);
        }

        public void Dispose() => _sync.Dispose();
    }
}
