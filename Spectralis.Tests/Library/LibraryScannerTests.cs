using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Library;
using Xunit;

namespace Spectralis.Tests.Library
{
    public class LibraryScannerTests : IDisposable
    {
        private readonly string _tempDir;

        public LibraryScannerTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"scan_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public async Task ScanAsync_EmptyFolder_ReturnsZeroTracks()
        {
            var scanner = new LibraryScanner();
            var tracks = await scanner.ScanAsync(_tempDir, CancellationToken.None);
            Assert.Empty(tracks);
        }

        [Fact]
        public async Task ScanAsync_WithFakeFiles_ReportsCorrectCount()
        {
            File.WriteAllText(Path.Combine(_tempDir, "a.mp3"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "b.flac"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "c.txt"), "ignore");
            var scanner = new LibraryScanner();
            var tracks = await scanner.ScanAsync(_tempDir, CancellationToken.None);
            Assert.Equal(2, tracks.Count);
        }

        [Fact]
        public async Task ScanAsync_Cancelled_StopsEarly()
        {
            for (int i = 0; i < 20; i++)
                File.WriteAllText(Path.Combine(_tempDir, $"track{i}.mp3"), "fake");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var scanner = new LibraryScanner();
            var tracks = await scanner.ScanAsync(_tempDir, cts.Token);
            Assert.True(tracks.Count < 20);
        }

        [Fact]
        public async Task ScanAsync_SubFolders_IncludesNestedFiles()
        {
            var sub = Path.Combine(_tempDir, "sub");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(_tempDir, "top.mp3"), "fake");
            File.WriteAllText(Path.Combine(sub, "nested.mp3"), "fake");
            var scanner = new LibraryScanner();
            var tracks = await scanner.ScanAsync(_tempDir, CancellationToken.None);
            Assert.Equal(2, tracks.Count);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
    }
}
