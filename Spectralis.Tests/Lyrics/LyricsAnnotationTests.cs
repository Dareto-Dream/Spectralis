using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Lyrics;
using Xunit;

namespace Spectralis.Tests.Lyrics
{
    public class LyricsAnnotationTests : IDisposable
    {
        private readonly LyricsAnnotationStore _store = new();
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private readonly string _audioPath;

        public LyricsAnnotationTests()
        {
            Directory.CreateDirectory(_tempDir);
            _audioPath = Path.Combine(_tempDir, "track.mp3");
            File.WriteAllText(_audioPath, string.Empty);
        }

        [Fact]
        public async Task SaveAndLoad_RoundTrip()
        {
            var file = new LyricsAnnotationFile { TrackTitle = "Test", Artist = "Artist" };
            var ann = new LyricsAnnotation
            {
                TimestampKey = "00:05.000",
                Timestamp = TimeSpan.FromSeconds(5),
                LineText = "Hello world",
                Explanation = "A greeting"
            };
            _store.Upsert(file, ann);
            await _store.SaveAsync(_audioPath, file);

            var loaded = await _store.LoadAsync(_audioPath);
            Assert.NotNull(loaded);
            Assert.True(loaded!.Annotations.ContainsKey("00:05.000"));
            Assert.Equal("A greeting", loaded.Annotations["00:05.000"].Explanation);
        }

        [Fact]
        public async Task Load_ReturnsNull_WhenNoSidecar()
        {
            var result = await _store.LoadAsync(_audioPath);
            Assert.Null(result);
        }

        [Fact]
        public async Task Remove_DeletesAnnotation()
        {
            var file = new LyricsAnnotationFile();
            var ann = new LyricsAnnotation { TimestampKey = "00:10.000" };
            _store.Upsert(file, ann);
            await _store.SaveAsync(_audioPath, file);

            bool removed = _store.Remove(file, "00:10.000");
            Assert.True(removed);
            Assert.Empty(file.Annotations);
        }

        [Fact]
        public async Task Save_WritesAtomically()
        {
            var file = new LyricsAnnotationFile();
            await _store.SaveAsync(_audioPath, file);

            string sidecar = Path.Combine(_tempDir, "track.lrc.json");
            Assert.True(File.Exists(sidecar));
            Assert.False(File.Exists(sidecar + ".tmp"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
