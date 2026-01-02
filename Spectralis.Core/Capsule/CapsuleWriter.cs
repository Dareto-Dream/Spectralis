using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsuleWriter
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public async Task WriteAsync(CapsuleMetadata meta, string sourceDirectory, string outputPath)
        {
            string dir = Path.GetDirectoryName(outputPath)!;
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmp = outputPath + ".tmp";
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false))
            {
                var metaEntry = zip.CreateEntry("capsule.json", CompressionLevel.Optimal);
                await using var ms = metaEntry.Open();
                await JsonSerializer.SerializeAsync(ms, meta, _opts);

                foreach (var track in meta.Tracks)
                {
                    if (string.IsNullOrEmpty(track.AssetPath)) continue;
                    string src = Path.Combine(sourceDirectory, track.AssetPath);
                    if (!File.Exists(src)) continue;

                    var entry = zip.CreateEntry(track.AssetPath, CompressionLevel.NoCompression);
                    await using var es = entry.Open();
                    await using var audio = File.OpenRead(src);
                    await audio.CopyToAsync(es);
                }
            }

            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tmp, outputPath);
        }
    }
}
