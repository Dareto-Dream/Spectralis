using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LyricsLoaderTests : IDisposable
    {
        private readonly LyricsLoader _loader = new();
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        public LyricsLoaderTests() => Directory.CreateDirectory(_tempDir);

        [Fact]
        public async Task LoadForTrackAsync_FindsLrcSidecar()
        {
            string audio = Path.Combine(_tempDir, "track.mp3");
            File.WriteAllText(audio, string.Empty);
            File.WriteAllText(Path.Combine(_tempDir, "track.lrc"), "[00:01.00]Hello");

            var lrc = await _loader.LoadForTrackAsync(audio);
            Assert.NotNull(lrc);
            Assert.Single(lrc!.Lines);
            Assert.Equal("Hello", lrc.Lines[0].Text);
        }

        [Fact]
        public async Task LoadForTrackAsync_ReturnsNullWhenNoSidecar()
        {
            string audio = Path.Combine(_tempDir, "nolyrics.mp3");
            File.WriteAllText(audio, string.Empty);

            var lrc = await _loader.LoadForTrackAsync(audio);
            Assert.Null(lrc);
        }

        [Fact]
        public async Task LoadForTrackAsync_NullPath_ReturnsNull()
        {
            var lrc = await _loader.LoadForTrackAsync(null!);
            Assert.Null(lrc);
        }

        [Fact]
        public void ParseInline_ParsesContent()
        {
            var lrc = _loader.ParseInline("[00:05.00]Inline line");
            Assert.NotNull(lrc);
            Assert.Single(lrc!.Lines);
        }

        [Fact]
        public void ParseInline_EmptyString_ReturnsNull()
        {
            var lrc = _loader.ParseInline(string.Empty);
            Assert.Null(lrc);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
