using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsuleReadResult
    {
        public CapsuleMetadata? Metadata { get; init; }
        public string? ExtractedPath { get; init; }
        public bool Success { get; init; }
        public string? Error { get; init; }
    }

    public class CapsuleReader
    {
        private static readonly byte[] Magic = { 0x53, 0x50, 0x43, 0x4C };

        public async Task<CapsuleReadResult> ReadAsync(string capsulePath, string extractTo)
        {
            if (!File.Exists(capsulePath))
                return new CapsuleReadResult { Error = "File not found" };

            try
            {
                using var fs = File.OpenRead(capsulePath);
                await ValidateMagicAsync(fs);

                Directory.CreateDirectory(extractTo);
                fs.Position = 0;

                using var zip = new ZipArchive(fs, ZipArchiveMode.Read, leaveOpen: false);
                var metaEntry = zip.GetEntry("capsule.json")
                    ?? throw new InvalidDataException("Missing capsule.json");

                CapsuleMetadata? meta;
                using (var ms = new MemoryStream())
                {
                    await using var es = metaEntry.Open();
                    await es.CopyToAsync(ms);
                    meta = JsonSerializer.Deserialize<CapsuleMetadata>(ms.ToArray());
                }

                if (meta == null) return new CapsuleReadResult { Error = "Malformed capsule.json" };

                foreach (var entry in zip.Entries)
                {
                    if (entry.Name == "capsule.json") continue;
                    string dest = Path.Combine(extractTo, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await using var os = File.Create(dest);
                    await using var es = entry.Open();
                    await es.CopyToAsync(os);
                }

                return new CapsuleReadResult { Metadata = meta, ExtractedPath = extractTo, Success = true };
            }
            catch (Exception ex)
            {
                return new CapsuleReadResult { Error = ex.Message };
            }
        }

        private static async Task ValidateMagicAsync(Stream stream)
        {
            var buf = new byte[4];
            int read = await stream.ReadAsync(buf);
            if (read < 4) return;
        }
    }
}
