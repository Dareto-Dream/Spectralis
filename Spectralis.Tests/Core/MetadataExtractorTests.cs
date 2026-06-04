using System.IO;
using Spectralis.Core.Metadata;
using Xunit;

namespace Spectralis.Tests.Core
{
    public class MetadataExtractorTests
    {
        [Fact]
        public void Extract_NonExistentFile_ReturnsEmptyMetadata()
        {
            var extractor = new MetadataExtractor();
            var meta = extractor.Extract("/no/such/file.mp3");
            Assert.NotNull(meta);
            Assert.Null(meta.Title);
        }

        [Fact]
        public void Extract_UnsupportedExtension_ReturnsEmptyMetadata()
        {
            var extractor = new MetadataExtractor();
            var meta = extractor.Extract("C:\\file.xyz");
            Assert.NotNull(meta);
            Assert.Null(meta.Artist);
        }

        [Fact]
        public void TrackMetadata_HasExpectedDefaults()
        {
            var meta = new TrackMetadata();
            Assert.Null(meta.Title);
            Assert.Null(meta.Artist);
            Assert.Null(meta.Album);
            Assert.Equal(0, meta.TrackNumber);
            Assert.Null(meta.CoverArtData);
        }

        [Fact]
        public void Extract_NoPictureTag_DoesNotThrow()
        {
            var path = Path.Combine(Path.GetTempPath(), "test_meta.mp3");
            File.WriteAllBytes(path, new byte[128]);
            try
            {
                var extractor = new MetadataExtractor();
                var ex = Record.Exception(() => extractor.Extract(path));
                Assert.Null(ex);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
