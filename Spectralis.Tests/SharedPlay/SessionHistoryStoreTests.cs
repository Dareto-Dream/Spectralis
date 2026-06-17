using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.SharedPlay
{
    public class SessionHistoryStoreTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private SessionHistoryStore Store => new(Path.Combine(_tempDir, "history.json"));

        public SessionHistoryStoreTests() => Directory.CreateDirectory(_tempDir);

        [Fact]
        public async Task Append_And_Load_Roundtrip()
        {
            var store = Store;
            var stats = new SharedPlayStats
            {
                SessionId = "sess1",
                RoomCode = "ABC123",
                PeakListeners = 5,
                StartedAt = DateTimeOffset.UtcNow
            };
            await store.AppendAsync(stats);
            var loaded = await store.LoadAsync();
            Assert.Single(loaded);
            Assert.Equal("ABC123", loaded[0].RoomCode);
        }

        [Fact]
        public async Task Load_ReturnsEmpty_WhenNoFile()
        {
            var result = await Store.LoadAsync();
            Assert.Empty(result);
        }

        [Fact]
        public async Task Append_Multiple_RetainsOrder()
        {
            var store = Store;
            await store.AppendAsync(new SharedPlayStats { RoomCode = "AAAA" });
            await store.AppendAsync(new SharedPlayStats { RoomCode = "BBBB" });
            var loaded = await store.LoadAsync();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("AAAA", loaded[0].RoomCode);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
