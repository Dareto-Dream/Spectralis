using System;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Spectralis.Streaming
{
    public class StreamingThumbnailCache : IDisposable
    {
        private readonly string _cacheDir;
        private readonly HttpClient _http;

        public StreamingThumbnailCache(string cacheDir)
        {
            _cacheDir = cacheDir;
            Directory.CreateDirectory(cacheDir);
            _http = new HttpClient();
            _http.Timeout = TimeSpan.FromSeconds(10);
        }

        public async Task<Image> GetAsync(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            string path = GetCachePath(url);
            if (File.Exists(path))
            {
                try { return Image.FromFile(path); } catch { }
            }

            try
            {
                byte[] data = await _http.GetByteArrayAsync(url);
                File.WriteAllBytes(path, data);
                return Image.FromStream(new MemoryStream(data));
            }
            catch
            {
                return null;
            }
        }

        private string GetCachePath(string url)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(url));
                string hex = BitConverter.ToString(hash).Replace("-", "").ToLower();
                return Path.Combine(_cacheDir, hex + ".jpg");
            }
        }

        public void Purge()
        {
            foreach (string f in Directory.GetFiles(_cacheDir, "*.jpg"))
            {
                try { File.Delete(f); } catch { }
            }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
