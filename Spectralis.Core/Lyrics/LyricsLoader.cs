using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Spectralis.Core.Lyrics
{
    public class LyricsLoader
    {
        private readonly LrcParser _parser = new();

        public async Task<LrcFile?> LoadForTrackAsync(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath)) return null;

            string dir = Path.GetDirectoryName(audioFilePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(audioFilePath);

            string lrcPath = Path.Combine(dir, name + ".lrc");
            if (File.Exists(lrcPath))
                return await ParseFileAsync(lrcPath);

            string lrcPathUpper = Path.Combine(dir, name + ".LRC");
            if (File.Exists(lrcPathUpper))
                return await ParseFileAsync(lrcPathUpper);

            return null;
        }

        private async Task<LrcFile?> ParseFileAsync(string path)
        {
            try
            {
                string content = await File.ReadAllTextAsync(path, Encoding.UTF8);
                return _parser.Parse(content);
            }
            catch
            {
                return null;
            }
        }

        public LrcFile? ParseInline(string lrcContent)
        {
            if (string.IsNullOrEmpty(lrcContent)) return null;
            return _parser.Parse(lrcContent);
        }
    }
}
