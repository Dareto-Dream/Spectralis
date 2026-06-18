using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Spectralis.Core.Audio;
using Spectralis.Core.Infrastructure;
using Spectralis.Core.Library;
using Spectralis.Core.Models;

namespace Spectralis.App.Services
{
    public class CoverArtService : IDisposable
    {
        private readonly CoverArtStore _store;
        private readonly MetadataExtractor _extractor;
        private bool _disposed;

        public CoverArtService(CoverArtStore store)
        {
            _store = store;
            _extractor = new MetadataExtractor();
        }

        public async Task<Bitmap?> GetBitmapAsync(TrackInfo track)
        {
            if (track.CoverArtBytes != null && track.CoverArtBytes.Length > 0)
                return DecodeBitmap(track.CoverArtBytes);

            byte[]? cached = await _store.GetBytesAsync(track.FilePath);
            if (cached != null) return DecodeBitmap(cached);

            if (!string.IsNullOrEmpty(track.FilePath) && File.Exists(track.FilePath))
            {
                var full = _extractor.Extract(track.FilePath);
                if (full.CoverArtBytes != null && full.CoverArtBytes.Length > 0)
                {
                    await _store.StoreAsync(track.FilePath, full.CoverArtBytes);
                    return DecodeBitmap(full.CoverArtBytes);
                }
            }

            return null;
        }

        private static Bitmap? DecodeBitmap(byte[] bytes)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
