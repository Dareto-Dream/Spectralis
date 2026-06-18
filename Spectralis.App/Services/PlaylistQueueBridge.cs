using System.Collections.Generic;
using System.Threading.Tasks;
using Spectralis.Core.Library;
using Spectralis.Core.Models;
using Spectralis.Core.Playlists;
using Spectralis.Core.Queue;

namespace Spectralis.App.Services
{
    public class PlaylistQueueBridge
    {
        private readonly PlayQueue _queue;
        private readonly LibraryManager _library;

        public PlaylistQueueBridge(PlayQueue queue, LibraryManager library)
        {
            _queue = queue;
            _library = library;
        }

        public async Task EnqueuePlaylistAsync(Playlist playlist, bool replaceQueue = false)
        {
            var tracks = await _library.GetTracksByIdsAsync(playlist.TrackIds);

            if (replaceQueue) _queue.Clear();

            foreach (var track in tracks)
                _queue.Add(new PlayQueueItem(track));

            if (replaceQueue && _queue.Count > 0)
                _queue.PlayAt(0);
        }

        public async Task EnqueueTracksAsync(IEnumerable<TrackInfo> tracks, bool replaceQueue = false)
        {
            if (replaceQueue) _queue.Clear();

            foreach (var track in tracks)
                _queue.Add(new PlayQueueItem(track));

            if (replaceQueue && _queue.Count > 0)
                _queue.PlayAt(0);

            await Task.CompletedTask;
        }
    }
}
