using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Integration
{
    public class CapsuleIntegrationTests : IDisposable
    {
        private readonly string _tempDir;

        public CapsuleIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"capint_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public async Task SignPackVerify_RoundTrip()
        {
            var (pubKey, privKey) = CapsuleSigner.GenerateKeyPair();

            var manifest = new CapsuleManifest
            {
                Id = Guid.NewGuid().ToString(),
                Title = "Integration Test",
                Artist = "Tester",
                Version = "5.0",
                EntryPoint = "index.html"
            };

            var signature = CapsuleSigner.Sign(manifest, privKey);
            manifest.Trust = new CapsuleTrust
            {
                PublicKeyBase64 = pubKey,
                SignatureBase64 = signature
            };

            bool valid = CapsuleSigner.Verify(manifest);
            Assert.True(valid);
        }

        [Fact]
        public async Task PackThenReadManifest_PreservesData()
        {
            var sourceDir = Path.Combine(_tempDir, "source");
            Directory.CreateDirectory(sourceDir);
            File.WriteAllText(Path.Combine(sourceDir, "index.html"), "<html></html>");

            var manifest = new CapsuleManifest
            {
                Id = "test-cap-001",
                Title = "Pack Test",
                Artist = "Artist",
                Version = "5.0",
                EntryPoint = "index.html"
            };

            var outputPath = Path.Combine(_tempDir, "test.capsule");
            await CapsulePackager.PackAsync(manifest, sourceDir, outputPath);

            Assert.True(File.Exists(outputPath));

            var readBack = await CapsulePackager.ReadManifestAsync(outputPath);
            Assert.Equal("Pack Test", readBack.Title);
            Assert.Equal("test-cap-001", readBack.Id);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
        }
    }
}
