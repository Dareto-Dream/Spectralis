using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Spectralis.Core.Library;
using Spectralis.Core.Models;

namespace Spectralis.Core.Playlists
{
    public class PlaylistManager : IDisposable
    {
        private readonly PlaylistRepository _repo;
        private readonly SmartPlaylistEvaluator _evaluator;
        private bool _disposed;

        public event EventHandler? PlaylistsChanged;

        public PlaylistManager(LibraryDb db)
        {
            _repo = new PlaylistRepository(db);
            _evaluator = new SmartPlaylistEvaluator();
        }

        public Task<List<Playlist>> GetAllAsync() => _repo.GetAllAsync();

        public async Task CreateAsync(Playlist playlist)
        {
            await _repo.UpsertAsync(playlist);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task UpdateAsync(Playlist playlist)
        {
            playlist.UpdatedAt = DateTime.UtcNow;
            await _repo.UpsertAsync(playlist);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task DeleteAsync(Guid id)
        {
            await _repo.DeleteAsync(id);
            PlaylistsChanged?.Invoke(this, EventArgs.Empty);
        }

        public async Task AddTrackAsync(Guid playlistId, string filePath)
        {
            var paths = await _repo.GetTrackPathsAsync(playlistId);
            await _repo.AddTrackAsync(playlistId, filePath, paths.Count);
        }

        public Task RemoveTrackAsync(Guid playlistId, string filePath)
            => _repo.RemoveTrackAsync(playlistId, filePath);

        public Task<List<string>> GetTrackPathsAsync(Guid playlistId)
            => _repo.GetTrackPathsAsync(playlistId);

        public IReadOnlyList<TrackInfo> EvaluateSmart(Playlist playlist, IEnumerable<TrackInfo> library)
            => _evaluator.Evaluate(playlist, library);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
