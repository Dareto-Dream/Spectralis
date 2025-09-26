using System;
using System.IO;

namespace Spectralis.Core.Library
{
    public class AlbumArtResolver
    {
        private static readonly string[] _filenames =
        {
            "cover", "folder", "album", "front", "artwork", "art"
        };

        private static readonly string[] _extensions =
        {
            ".jpg", ".jpeg", ".png", ".webp"
        };

        public string? FindFolderArt(string trackFilePath)
        {
            string? dir = Path.GetDirectoryName(trackFilePath);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return null;

            foreach (string name in _filenames)
            {
                foreach (string ext in _extensions)
                {
                    string candidate = Path.Combine(dir, name + ext);
                    if (File.Exists(candidate)) return candidate;

                    candidate = Path.Combine(dir, name.ToUpperInvariant() + ext);
                    if (File.Exists(candidate)) return candidate;
                }
            }

            foreach (string ext in _extensions)
            {
                var matches = Directory.GetFiles(dir, "*" + ext, SearchOption.TopDirectoryOnly);
                if (matches.Length == 1) return matches[0];
            }

            return null;
        }

        public byte[]? ReadFolderArtBytes(string trackFilePath)
        {
            string? path = FindFolderArt(trackFilePath);
            return path != null ? File.ReadAllBytes(path) : null;
        }
    }
}
