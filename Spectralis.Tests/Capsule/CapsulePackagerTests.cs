using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Capsule
{
    public class CapsulePackagerTests : IDisposable
    {
        private readonly CapsulePackager _packager = new();
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

        public CapsulePackagerTests() => Directory.CreateDirectory(_tempDir);

        [Fact]
        public async Task PackAsync_CreatesZipWithManifest()
        {
            string src = Path.Combine(_tempDir, "src");
            Directory.CreateDirectory(src);
            File.WriteAllText(Path.Combine(src, "world.html"), "<html>hello</html>");

            string outPath = Path.Combine(_tempDir, "test.spectral");
            var manifest = new CapsuleManifest { Title = "Test", Artist = "Artist" };
            await _packager.PackAsync(manifest, src, outPath);

            Assert.True(File.Exists(outPath));
            Assert.False(File.Exists(outPath + ".tmp"));
        }

        [Fact]
        public async Task ReadManifestAsync_ReturnsNull_WhenFileNotFound()
        {
            var result = await _packager.ReadManifestAsync(Path.Combine(_tempDir, "missing.spectral"));
            Assert.Null(result);
        }

        [Fact]
        public async Task PackAndRead_RoundTrip()
        {
            string src = Path.Combine(_tempDir, "src2");
            Directory.CreateDirectory(src);

            string outPath = Path.Combine(_tempDir, "album.spectral");
            var manifest = new CapsuleManifest { Title = "Round Trip", Artist = "Band" };
            manifest.Tracks.Add(new CapsuleTrack { Id = "t1", Title = "Track 1" });

            await _packager.PackAsync(manifest, src, outPath);
            var loaded = await _packager.ReadManifestAsync(outPath);

            Assert.NotNull(loaded);
            Assert.Equal("Round Trip", loaded!.Title);
            Assert.Single(loaded.Tracks);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
