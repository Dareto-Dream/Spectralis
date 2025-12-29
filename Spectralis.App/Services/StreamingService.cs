using System;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Audio;
using Spectralis.Core.Queue;
using Spectralis.Core.Streaming;

namespace Spectralis.App.Services
{
    public sealed class StreamingService : IDisposable
    {
        private readonly StreamingRegistry _registry;
        private readonly IAudioEngine _engine;
        private readonly PlayQueue _queue;
        private bool _disposed;

        public StreamingService(StreamingRegistry registry, IAudioEngine engine, PlayQueue queue)
        {
            _registry = registry;
            _engine = engine;
            _queue = queue;
        }

        public async Task<bool> PlayAsync(string sourceId, string trackId, TrackInfo trackInfo, CancellationToken ct = default)
        {
            var stream = await _registry.OpenStreamAsync(sourceId, trackId, ct);
            if (stream == null) return false;
            await _engine.LoadStreamAsync(stream, trackInfo);
            await _engine.PlayAsync();
            return true;
        }

        public void EnqueueTrack(TrackInfo track)
        {
            _queue.Enqueue(new PlayQueueItem { Track = track });
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
