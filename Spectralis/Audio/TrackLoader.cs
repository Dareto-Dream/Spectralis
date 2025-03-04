using System;
using System.IO;

namespace Spectralis.Audio
{
    public class TrackLoader
    {
        public event EventHandler<TrackInfo> TrackReady;
        public event EventHandler<string> LoadError;

        public void LoadAsync(string filePath)
        {
            if (!FormatDetector.IsSupported(filePath))
            {
                LoadError?.Invoke(this, $"Unsupported format: {Path.GetExtension(filePath)}");
                return;
            }

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var info = MetadataExtractor.Extract(filePath);
                    TrackReady?.Invoke(this, info);
                }
                catch (Exception ex)
                {
                    LoadError?.Invoke(this, ex.Message);
                }
            });
        }

        public (IAudioReader reader, TrackInfo info) Load(string filePath)
        {
            if (!FormatDetector.IsSupported(filePath))
                throw new NotSupportedException($"Unsupported format: {Path.GetExtension(filePath)}");

            var info = MetadataExtractor.Extract(filePath);
            var reader = FormatDetector.CreateReader(filePath);
            return (reader, info);
        }
    }
}
