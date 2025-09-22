using System;
using System.IO;
using TagLib;

namespace Spectralis.Core.Audio
{
    public class ReplayGainInfo
    {
        public float? TrackGain { get; set; }
        public float? TrackPeak { get; set; }
        public float? AlbumGain { get; set; }
        public float? AlbumPeak { get; set; }

        public bool HasTrackGain => TrackGain.HasValue;
        public bool HasAlbumGain => AlbumGain.HasValue;
    }

    public class ReplayGainReader
    {
        public ReplayGainInfo? Read(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var f = TagLib.File.Create(filePath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();

                float? trackGain = null, trackPeak = null, albumGain = null, albumPeak = null;

                if (f.Tag is TagLib.Ogg.XiphComment xiph)
                {
                    trackGain = ParseGain(xiph.GetField("REPLAYGAIN_TRACK_GAIN"));
                    trackPeak = ParsePeak(xiph.GetField("REPLAYGAIN_TRACK_PEAK"));
                    albumGain = ParseGain(xiph.GetField("REPLAYGAIN_ALBUM_GAIN"));
                    albumPeak = ParsePeak(xiph.GetField("REPLAYGAIN_ALBUM_PEAK"));
                }
                else if (f.Tag is TagLib.Id3v2.Tag id3)
                {
                    trackGain = ParseGain(GetId3RgFrame(id3, "REPLAYGAIN_TRACK_GAIN"));
                    trackPeak = ParsePeak(GetId3RgFrame(id3, "REPLAYGAIN_TRACK_PEAK"));
                    albumGain = ParseGain(GetId3RgFrame(id3, "REPLAYGAIN_ALBUM_GAIN"));
                    albumPeak = ParsePeak(GetId3RgFrame(id3, "REPLAYGAIN_ALBUM_PEAK"));
                }

                if (!trackGain.HasValue && !albumGain.HasValue) return null;

                return new ReplayGainInfo
                {
                    TrackGain = trackGain,
                    TrackPeak = trackPeak,
                    AlbumGain = albumGain,
                    AlbumPeak = albumPeak
                };
            }
            catch { return null; }
        }

        public float ComputeScalar(ReplayGainInfo info, bool preferAlbum = false, float preampDb = 0f)
        {
            float? gain = preferAlbum && info.HasAlbumGain ? info.AlbumGain : info.TrackGain;
            if (!gain.HasValue) return 1f;
            return (float)Math.Pow(10, (gain.Value + preampDb) / 20.0);
        }

        private static float? ParseGain(string? val)
        {
            if (string.IsNullOrEmpty(val)) return null;
            val = val.Replace("dB", "").Replace("db", "").Trim();
            return float.TryParse(val, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;
        }

        private static float? ParsePeak(string? val) =>
            float.TryParse(val?.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var f) ? f : null;

        private static string? GetId3RgFrame(TagLib.Id3v2.Tag tag, string key)
        {
            foreach (var frame in tag.GetFrames<TagLib.Id3v2.UserTextInformationFrame>())
                if (string.Equals(frame.Description, key, StringComparison.OrdinalIgnoreCase))
                    return frame.Text?.Length > 0 ? frame.Text[0] : null;
            return null;
        }
    }
}
