using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Spectralis.Library
{
    public class CoverArtStore
    {
        private readonly string _storePath;

        public CoverArtStore(string appDataPath)
        {
            _storePath = Path.Combine(appDataPath, "covers");
            Directory.CreateDirectory(_storePath);
        }

        public string Save(string trackPath, byte[] imageData)
        {
            if (imageData == null || imageData.Length == 0) return null;

            string hash = ComputeHash(trackPath);
            string coverPath = Path.Combine(_storePath, $"{hash}.jpg");

            if (File.Exists(coverPath)) return coverPath;

            try
            {
                using var ms = new MemoryStream(imageData);
                using var img = Image.FromStream(ms);
                using var thumb = img.GetThumbnailImage(256, 256, null, IntPtr.Zero);
                thumb.Save(coverPath, ImageFormat.Jpeg);
                return coverPath;
            }
            catch
            {
                return null;
            }
        }

        public Image Load(string coverPath)
        {
            if (string.IsNullOrEmpty(coverPath) || !File.Exists(coverPath)) return null;
            try { return Image.FromFile(coverPath); }
            catch { return null; }
        }

        public void Delete(string coverPath)
        {
            try { if (File.Exists(coverPath)) File.Delete(coverPath); }
            catch { }
        }

        private static string ComputeHash(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            return BitConverter.ToString(bytes, 0, 8).Replace("-", "").ToLowerInvariant();
        }
    }
}
