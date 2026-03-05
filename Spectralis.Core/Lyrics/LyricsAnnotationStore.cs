using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Lyrics
{
    public class LyricsAnnotationStore
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public async Task<LyricsAnnotationFile?> LoadAsync(string audioFilePath)
        {
            string path = GetSidecarPath(audioFilePath);
            if (!File.Exists(path)) return null;
            try
            {
                string text = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<LyricsAnnotationFile>(text, _json);
            }
            catch { return null; }
        }

        public async Task SaveAsync(string audioFilePath, LyricsAnnotationFile file)
        {
            string path = GetSidecarPath(audioFilePath);
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(file, _json);
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public void Upsert(LyricsAnnotationFile file, LyricsAnnotation annotation)
        {
            file.Annotations[annotation.TimestampKey] = annotation;
        }

        public bool Remove(LyricsAnnotationFile file, string timestampKey)
        {
            return file.Annotations.Remove(timestampKey);
        }

        private static string GetSidecarPath(string audioFilePath)
        {
            string dir = Path.GetDirectoryName(audioFilePath) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(audioFilePath);
            return Path.Combine(dir, name + ".lrc.json");
        }
    }
}
