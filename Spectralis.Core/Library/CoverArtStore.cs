using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Spectralis.Core.Library
{
    public class CoverArtStore
    {
        private readonly string _cacheDir;

        public CoverArtStore(string cacheDir)
        {
            _cacheDir = cacheDir;
            if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);
        }

        public async Task<string?> GetPathAsync(string filePath)
        {
            string key = HashPath(filePath);
            string cached = Path.Combine(_cacheDir, key + ".jpg");
            if (File.Exists(cached)) return cached;
            return null;
        }

        public async Task<string?> StoreAsync(string filePath, byte[] jpegBytes)
        {
            string key = HashPath(filePath);
            string dest = Path.Combine(_cacheDir, key + ".jpg");
            await File.WriteAllBytesAsync(dest, jpegBytes);
            return dest;
        }

        public async Task<byte[]?> GetBytesAsync(string filePath)
        {
            string? path = await GetPathAsync(filePath);
            if (path == null) return null;
            return await File.ReadAllBytesAsync(path);
        }

        public void Evict(string filePath)
        {
            string key = HashPath(filePath);
            string cached = Path.Combine(_cacheDir, key + ".jpg");
            if (File.Exists(cached)) File.Delete(cached);
        }

        public void PurgeAll()
        {
            foreach (string f in Directory.GetFiles(_cacheDir, "*.jpg"))
                File.Delete(f);
        }

        public long GetCacheSizeBytes()
        {
            long total = 0;
            foreach (string f in Directory.GetFiles(_cacheDir, "*.jpg"))
                total += new FileInfo(f).Length;
            return total;
        }

        private static string HashPath(string filePath)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
            return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
        }
    }
}
