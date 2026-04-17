using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Capsule
{
    public class CapsuleSignerTests
    {
        private readonly CapsuleSigner _signer = new();

        [Fact]
        public void GenerateKeyPair_ReturnsTwoDistinctKeys()
        {
            var (pub, priv) = CapsuleSigner.GenerateKeyPair();
            Assert.NotEmpty(pub);
            Assert.NotEmpty(priv);
            Assert.NotEqual(pub, priv);
        }

        [Fact]
        public void Sign_And_Verify_Roundtrip()
        {
            var (pub, priv) = CapsuleSigner.GenerateKeyPair();
            var manifest = new CapsuleManifest { Title = "Test Album", Artist = "Artist" };
            manifest.Trust = _signer.Sign(manifest, priv, pub);

            Assert.True(_signer.Verify(manifest));
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenSignatureTampered()
        {
            var (pub, priv) = CapsuleSigner.GenerateKeyPair();
            var manifest = new CapsuleManifest { Title = "Test", Artist = "Artist" };
            manifest.Trust = _signer.Sign(manifest, priv, pub);
            manifest.Trust.SignatureBase64 = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

            Assert.False(_signer.Verify(manifest));
        }

        [Fact]
        public void Verify_ReturnsFalse_WhenNoSignature()
        {
            var manifest = new CapsuleManifest { Title = "Test", Artist = "Artist" };
            Assert.False(_signer.Verify(manifest));
        }

        [Fact]
        public void Sign_SetsSignedAt()
        {
            var (pub, priv) = CapsuleSigner.GenerateKeyPair();
            var manifest = new CapsuleManifest();
            manifest.Trust = _signer.Sign(manifest, priv, pub);
            Assert.NotNull(manifest.Trust.SignedAt);
        }
    }
}
