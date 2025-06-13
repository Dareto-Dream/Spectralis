using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public interface ILibrary
    {
        event EventHandler<TrackInfo>? TrackAdded;
        event EventHandler<TrackInfo>? TrackRemoved;
        event EventHandler? LibraryChanged;

        Task<IReadOnlyList<TrackInfo>> SearchAsync(string query, CancellationToken ct = default);
        Task<IReadOnlyList<TrackInfo>> GetAllAsync(CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetArtistsAsync(CancellationToken ct = default);
        Task<IReadOnlyList<string>> GetAlbumsAsync(string? artist = null, CancellationToken ct = default);
        Task<int> CountAsync(CancellationToken ct = default);
        Task AddFolderAsync(string folderPath, IProgress<string>? progress = null, CancellationToken ct = default);
    }
}
