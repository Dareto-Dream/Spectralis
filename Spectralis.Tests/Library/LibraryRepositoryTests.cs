using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Library;
using Xunit;

namespace Spectralis.Tests.Library
{
    public class LibraryRepositoryTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly LibraryRepository _repo;

        public LibraryRepositoryTests()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
            _repo = new LibraryRepository(_dbPath);
        }

        [Fact]
        public async Task AddTrack_ThenGetById_ReturnsTrack()
        {
            var track = new LibraryTrack { FilePath = "/music/test.mp3", Title = "Test Song", Artist = "Artist", Album = "Album", DurationSeconds = 180 };
            int id = await _repo.AddAsync(track);
            var result = await _repo.GetByIdAsync(id);
            Assert.NotNull(result);
            Assert.Equal("Test Song", result!.Title);
        }

        [Fact]
        public async Task GetAll_AfterMultipleAdds_ReturnsAll()
        {
            for (int i = 0; i < 5; i++)
                await _repo.AddAsync(new LibraryTrack { FilePath = $"/music/track{i}.mp3", Title = $"Track {i}" });
            var all = await _repo.GetAllAsync();
            Assert.True(all.Count >= 5);
        }

        [Fact]
        public async Task DeleteTrack_RemovesFromDb()
        {
            int id = await _repo.AddAsync(new LibraryTrack { FilePath = "/music/delete.mp3", Title = "Delete Me" });
            await _repo.DeleteAsync(id);
            var result = await _repo.GetByIdAsync(id);
            Assert.Null(result);
        }

        [Fact]
        public async Task UpdateTrack_PersistsChanges()
        {
            int id = await _repo.AddAsync(new LibraryTrack { FilePath = "/music/update.mp3", Title = "Old Title" });
            var track = await _repo.GetByIdAsync(id);
            track!.Title = "New Title";
            await _repo.UpdateAsync(track);
            var updated = await _repo.GetByIdAsync(id);
            Assert.Equal("New Title", updated!.Title);
        }

        public void Dispose()
        {
            _repo.Dispose();
            if (File.Exists(_dbPath)) File.Delete(_dbPath);
        }
    }
}
