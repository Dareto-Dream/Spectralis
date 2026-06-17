using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Capsule
{
    public class CapsuleTrustStoreTests : IDisposable
    {
        private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        private CapsuleTrustStore Store => new(Path.Combine(_tempDir, "trust.json"));

        public CapsuleTrustStoreTests() => Directory.CreateDirectory(_tempDir);

        [Fact]
        public async Task Trust_And_IsTrusted_Roundtrip()
        {
            var store = Store;
            await store.TrustAsync("cap-1", "pubkey123");
            await store.LoadAsync();
            Assert.True(store.IsTrusted("cap-1", "pubkey123"));
        }

        [Fact]
        public async Task IsTrusted_ReturnsFalse_WhenNotTrusted()
        {
            var store = Store;
            await store.LoadAsync();
            Assert.False(store.IsTrusted("cap-unknown", "anykey"));
        }

        [Fact]
        public async Task Revoke_RemovesTrust()
        {
            var store = Store;
            await store.TrustAsync("cap-2", "pubkey456");
            await store.RevokeAsync("cap-2");
            await store.LoadAsync();
            Assert.False(store.IsTrusted("cap-2", "pubkey456"));
        }

        [Fact]
        public async Task IsTrusted_ReturnsFalse_WhenKeyMismatch()
        {
            var store = Store;
            await store.TrustAsync("cap-3", "rightkey");
            Assert.False(store.IsTrusted("cap-3", "wrongkey"));
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }
}
