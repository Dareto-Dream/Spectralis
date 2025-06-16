using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Streaming
{
    public record StreamingTrack(
        string Id,
        string Title,
        string Artist,
        string Album,
        TimeSpan Duration,
        string? ThumbnailUrl,
        string Source,
        string? StreamUrl = null
    );

    public interface IStreamingSource : IDisposable
    {
        string Name { get; }
        bool IsAuthenticated { get; }

        Task<IReadOnlyList<StreamingTrack>> SearchAsync(string query, int limit = 20, CancellationToken ct = default);
        Task<Stream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default);
        Task<bool> AuthenticateAsync(CancellationToken ct = default);
    }
}
