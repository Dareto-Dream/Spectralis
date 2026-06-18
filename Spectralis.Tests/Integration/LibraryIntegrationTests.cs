using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Library;
using Xunit;

namespace Spectralis.Tests.Integration
{
    public class LibraryIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _dbPath;

        public LibraryIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"libint_{Guid.NewGuid():N}");
            _dbPath = Path.Combine(Path.GetTempPath(), $"libint_{Guid.NewGuid():N}.db");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public async Task ScanAndStore_RoundTrip()
        {
            File.WriteAllText(Path.Combine(_tempDir, "song.mp3"), "fake");
            File.WriteAllText(Path.Combine(_tempDir, "song2.flac"), "fake");

            var scanner = new LibraryScanner();
            var tracks = await scanner.ScanAsync(_tempDir, CancellationToken.None);

            var repo = new LibraryRepository(_dbPath);
            foreach (var track in tracks)
                await repo.AddAsync(track);

            var all = await repo.GetAllAsync();
            Assert.Equal(tracks.Count, all.Count);
            repo.Dispose();
        }

        [Fact]
        public async Task Scan_UpdatesExistingTrack_ByPath()
        {
            var path = Path.Combine(_tempDir, "update_test.mp3");
            File.WriteAllText(path, "fake");

            var repo = new LibraryRepository(_dbPath);
            int id = await repo.AddAsync(new LibraryTrack { FilePath = path, Title = "Old" });

            var updated = await repo.GetByIdAsync(id);
            updated!.Title = "New";
            await repo.UpdateAsync(updated);

            var result = await repo.GetByIdAsync(id);
            Assert.Equal("New", result!.Title);
            repo.Dispose();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
    }
}
