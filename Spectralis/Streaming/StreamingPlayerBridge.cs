using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Spectralis.Audio;

namespace Spectralis.Streaming
{
    public class StreamingPlayerBridge : IDisposable
    {
        private readonly AudioEngine _engine;
        private readonly StreamingRegistry _registry;
        private readonly StreamingTrackCache _cache;
        private CancellationTokenSource _loadCts;
        private bool _disposed;

        public event EventHandler<string> StatusChanged;
        public event EventHandler<StreamingTrack> TrackLoaded;

        public StreamingPlayerBridge(AudioEngine engine, StreamingRegistry registry, StreamingTrackCache cache)
        {
            _engine = engine;
            _registry = registry;
            _cache = cache;
        }

        public async Task LoadAndPlayAsync(StreamingTrack track)
        {
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            var ct = _loadCts.Token;

            StatusChanged?.Invoke(this, $"Loading {track.Title} from {track.Source}...");

            if (!_registry.TryGet(track.Source, out var source))
                throw new InvalidOperationException($"No streaming source registered for {track.Source}.");

            WaveStream stream = await source.OpenStreamAsync(track, ct);
            if (stream == null)
                throw new InvalidOperationException("Streaming source returned no audio stream.");

            ct.ThrowIfCancellationRequested();

            TrackLoaded?.Invoke(this, track);
            StatusChanged?.Invoke(this, $"Playing: {track.Title}");
        }

        public void Cancel()
        {
            _loadCts?.Cancel();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _loadCts?.Cancel();
            _loadCts?.Dispose();
        }
    }
}
