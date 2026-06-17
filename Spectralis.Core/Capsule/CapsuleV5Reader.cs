using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsuleV5Reader
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task<CapsuleV5ReadResult> ReadAsync(string capsulePath, string extractDir)
        {
            if (!File.Exists(capsulePath))
                return new CapsuleV5ReadResult { Error = "File not found" };

            try
            {
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
                Directory.CreateDirectory(extractDir);
                ZipFile.ExtractToDirectory(capsulePath, extractDir);

                string manifestPath = Path.Combine(extractDir, "manifest.json");
                if (!File.Exists(manifestPath))
                    return new CapsuleV5ReadResult { Error = "No manifest.json in capsule" };

                string json = await File.ReadAllTextAsync(manifestPath);
                var manifest = JsonSerializer.Deserialize<CapsuleManifest>(json, _json);
                if (manifest == null)
                    return new CapsuleV5ReadResult { Error = "Failed to parse manifest" };

                return new CapsuleV5ReadResult { Manifest = manifest, ExtractedRoot = extractDir };
            }
            catch (System.Exception ex)
            {
                return new CapsuleV5ReadResult { Error = ex.Message };
            }
        }
    }

    public class CapsuleV5ReadResult
    {
        public bool Success => Error == null && Manifest != null;
        public CapsuleManifest? Manifest { get; set; }
        public string? ExtractedRoot { get; set; }
        public string? Error { get; set; }
    }
}
