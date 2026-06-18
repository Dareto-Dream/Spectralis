using FluentAssertions;
using Spectralis.Core.Analysis;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace Spectralis.Tests.Analysis
{
    public class AnalysisCacheTests
    {
        private static string TempCachePath() =>
            Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");

        private static AnalysisResult MakeResult(string path) => new()
        {
            FilePath = path,
            Bpm = new BpmResult { Bpm = 128f, Confidence = 0.85f },
            Key = new KeyResult { Name = "C Major", Confidence = 0.7f },
            LoudnessLufs = -14.5f
        };

        [Fact]
        public void Get_NoFile_ReturnsNull()
        {
            var cache = new AnalysisCache(TempCachePath());
            cache.Get("/nonexistent/file.mp3").Should().BeNull();
        }

        [Fact]
        public async Task Store_ThenGet_SameFile_ReturnsCached()
        {
            string tmpAudio = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tmpAudio, new byte[1024]);
                var cache = new AnalysisCache(TempCachePath());
                var result = MakeResult(tmpAudio);
                cache.Store(result);
                await cache.SaveAsync();

                var cache2 = new AnalysisCache(((object)cache).GetType()
                    .GetField("_filePath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(cache)!.ToString()!);

                var entry = cache2.Get(tmpAudio);
                entry.Should().NotBeNull();
                entry!.Bpm.Should().BeApproximately(128f, 0.01f);
            }
            finally { File.Delete(tmpAudio); }
        }

        [Fact]
        public void Invalidate_RemovesEntry()
        {
            string tmpAudio = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tmpAudio, new byte[512]);
                var cache = new AnalysisCache(TempCachePath());
                cache.Store(MakeResult(tmpAudio));
                cache.Invalidate(tmpAudio);
                cache.Get(tmpAudio).Should().BeNull();
            }
            finally { File.Delete(tmpAudio); }
        }

        [Fact]
        public void Get_FileSizeChanged_ReturnsNull()
        {
            string tmpAudio = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tmpAudio, new byte[512]);
                var cache = new AnalysisCache(TempCachePath());
                cache.Store(MakeResult(tmpAudio));

                File.WriteAllBytes(tmpAudio, new byte[1024]);
                cache.Get(tmpAudio).Should().BeNull();
            }
            finally { File.Delete(tmpAudio); }
        }
    }
}
