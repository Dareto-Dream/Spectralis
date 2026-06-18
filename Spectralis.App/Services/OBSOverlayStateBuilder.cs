using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Audio;

namespace Spectralis.App.Services
{
    public class OBSOverlayStateBuilder
    {
        private readonly CoverArtStore _coverArt;

        public OBSOverlayStateBuilder(CoverArtStore coverArt)
        {
            _coverArt = coverArt;
        }

        public async Task<OBSOverlayState> BuildAsync(TrackInfo track, TimeSpan position, TimeSpan duration, bool isPlaying)
        {
            string? coverBase64 = null;
            if (!string.IsNullOrEmpty(track.FilePath))
            {
                string? coverPath = _coverArt.GetCachedPath(track.FilePath);
                if (coverPath != null && File.Exists(coverPath))
                {
                    byte[] bytes = await File.ReadAllBytesAsync(coverPath);
                    coverBase64 = Convert.ToBase64String(bytes);
                }
            }

            return new OBSOverlayState
            {
                TrackTitle = track.Title ?? string.Empty,
                Artist = track.Artist ?? string.Empty,
                Album = track.Album ?? string.Empty,
                PositionSeconds = position.TotalSeconds,
                DurationSeconds = duration.TotalSeconds,
                IsPlaying = isPlaying,
                CoverArtBase64 = coverBase64
            };
        }
    }
}
