using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Capsule
{
    public class CapsuleRoundtripTests
    {
        private static CapsuleMetadata MakeMeta() => new()
        {
            Title = "Test Album",
            Artist = "Test Artist",
            Tracks = new List<CapsuleTrackRef>
            {
                new() { Id = "t1", Title = "Track 1", Artist = "Test Artist", AssetPath = "track1.mp3" }
            }
        };

        [Fact]
        public async Task Write_Then_Read_PreservesMetadata()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            string capsulePath = tempDir + ".spectralis";
            string audioSrc = Path.Combine(Path.GetTempPath(), "track1.mp3");
            string extractTo = tempDir + "_out";

            try
            {
                Directory.CreateDirectory(tempDir);
                File.WriteAllBytes(audioSrc, new byte[512]);

                var meta = MakeMeta();
                meta.Tracks[0].AssetPath = "track1.mp3";

                var writer = new CapsuleWriter();
                await writer.WriteAsync(meta, Path.GetTempPath(), capsulePath);

                var reader = new CapsuleReader();
                var result = await reader.ReadAsync(capsulePath, extractTo);

                result.Success.Should().BeTrue();
                result.Metadata.Should().NotBeNull();
                result.Metadata!.Title.Should().Be("Test Album");
                result.Metadata.Artist.Should().Be("Test Artist");
                result.Metadata.Tracks.Should().HaveCount(1);
            }
            finally
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                if (File.Exists(capsulePath)) File.Delete(capsulePath);
                if (Directory.Exists(extractTo)) Directory.Delete(extractTo, true);
                if (File.Exists(audioSrc)) File.Delete(audioSrc);
            }
        }

        [Fact]
        public async Task Read_NonExistentFile_ReturnsError()
        {
            var reader = new CapsuleReader();
            var result = await reader.ReadAsync("/does/not/exist.spectralis", Path.GetTempPath());
            result.Success.Should().BeFalse();
            result.Error.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public async Task Read_EmptyFile_ReturnsError()
        {
            string path = Path.GetTempFileName();
            try
            {
                var reader = new CapsuleReader();
                var result = await reader.ReadAsync(path, Path.GetTempPath());
                result.Success.Should().BeFalse();
            }
            finally { File.Delete(path); }
        }
    }
}
