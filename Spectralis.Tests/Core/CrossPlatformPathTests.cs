using System.IO;
using System.Runtime.InteropServices;
using Spectralis.Core;
using Xunit;

namespace Spectralis.Tests.Core
{
    public class CrossPlatformPathTests
    {
        [Fact]
        public void GetDataDirectory_ReturnsNonEmptyPath()
        {
            var dir = PlatformPaths.GetDataDirectory();
            Assert.False(string.IsNullOrEmpty(dir));
        }

        [Fact]
        public void GetCacheDirectory_ReturnsNonEmptyPath()
        {
            var dir = PlatformPaths.GetCacheDirectory();
            Assert.False(string.IsNullOrEmpty(dir));
        }

        [Fact]
        public void GetDataDirectory_IsAbsolute()
        {
            var dir = PlatformPaths.GetDataDirectory();
            Assert.True(Path.IsPathRooted(dir));
        }

        [Fact]
        public void GetLogDirectory_ContainsSpectralis()
        {
            var dir = PlatformPaths.GetLogDirectory();
            Assert.Contains("Spectralis", dir);
        }

        [Fact]
        public void EnsureDirectoryExists_CreatesDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), "spectralis_test_dir");
            try
            {
                PlatformPaths.EnsureExists(dir);
                Assert.True(Directory.Exists(dir));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir);
            }
        }
    }
}
