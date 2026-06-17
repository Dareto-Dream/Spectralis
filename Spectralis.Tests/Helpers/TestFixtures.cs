using System;
using System.IO;

namespace Spectralis.Tests.Helpers
{
    public static class TestFixtures
    {
        public static string CreateTempDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"spectralis_{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }

        public static string CreateTempDb()
            => Path.Combine(Path.GetTempPath(), $"spectralis_{Guid.NewGuid():N}.db");

        public static void CreateFakeAudioFiles(string dir, int count, string ext = "mp3")
        {
            for (int i = 0; i < count; i++)
                File.WriteAllBytes(Path.Combine(dir, $"track{i:D3}.{ext}"), new byte[512]);
        }
    }

    public sealed class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"spectest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, true);
        }
    }
}
