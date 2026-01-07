using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Audio;
using Spectralis.Core.Capsule;
using Spectralis.Core.Queue;

namespace Spectralis.App.Services
{
    public sealed class CapsuleService : IDisposable
    {
        private readonly CapsuleReader _reader;
        private readonly IAudioEngine _engine;
        private readonly PlayQueue _queue;
        private readonly string _extractRoot;
        private bool _disposed;

        public CapsuleService(CapsuleReader reader, IAudioEngine engine, PlayQueue queue, string extractRoot)
        {
            _reader = reader;
            _engine = engine;
            _queue = queue;
            _extractRoot = extractRoot;
        }

        public async Task<(bool Success, string? Error)> OpenAndPlayAsync(string capsulePath)
        {
            string extractTo = Path.Combine(_extractRoot, Path.GetFileNameWithoutExtension(capsulePath));
            var result = await _reader.ReadAsync(capsulePath, extractTo);

            if (!result.Success || result.Metadata == null)
                return (false, result.Error);

            _queue.Clear();
            var tracks = result.Metadata.Tracks;
            foreach (var trackRef in tracks)
            {
                string audioPath = Path.Combine(result.ExtractedPath!, trackRef.AssetPath);
                if (!File.Exists(audioPath)) continue;

                _queue.Enqueue(new PlayQueueItem
                {
                    Track = new TrackInfo
                    {
                        FilePath = audioPath,
                        Title = trackRef.Title,
                        Artist = trackRef.Artist,
                        DurationSeconds = trackRef.DurationSeconds
                    }
                });
            }

            var first = _queue.Current;
            if (first != null)
            {
                await _engine.LoadAsync(first.Track.FilePath);
                await _engine.PlayAsync();
            }

            return (true, null);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
