using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsulePackager
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

        public async Task PackAsync(CapsuleManifest manifest, string sourceDirectory, string outputPath)
        {
            string tmp = outputPath + ".tmp";
            try
            {
                using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
                {
                    string manifestJson = JsonSerializer.Serialize(manifest, _json);
                    var entry = zip.CreateEntry("manifest.json");
                    using (var sw = new StreamWriter(entry.Open()))
                        await sw.WriteAsync(manifestJson);

                    foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
                    {
                        string relative = Path.GetRelativePath(sourceDirectory, file);
                        zip.CreateEntryFromFile(file, relative.Replace('\\', '/'));
                    }
                }

                if (File.Exists(outputPath)) File.Delete(outputPath);
                File.Move(tmp, outputPath);
            }
            catch
            {
                if (File.Exists(tmp)) File.Delete(tmp);
                throw;
            }
        }

        public async Task<CapsuleManifest?> ReadManifestAsync(string capsulePath)
        {
            if (!File.Exists(capsulePath)) return null;
            try
            {
                using var zip = ZipFile.OpenRead(capsulePath);
                var entry = zip.GetEntry("manifest.json");
                if (entry == null) return null;
                using var reader = new StreamReader(entry.Open());
                string json = await reader.ReadToEndAsync();
                return JsonSerializer.Deserialize<CapsuleManifest>(json, _json);
            }
            catch { return null; }
        }
    }
}
