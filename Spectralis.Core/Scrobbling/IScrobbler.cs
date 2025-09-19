using System;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Scrobbling
{
    public class ScrobbleEntry
    {
        public TrackInfo Track { get; set; } = null!;
        public DateTimeOffset Timestamp { get; set; }
        public int DurationSeconds { get; set; }
    }

    public interface IScrobbler : IDisposable
    {
        string ServiceName { get; }
        bool IsAuthenticated { get; }

        Task<bool> AuthenticateAsync(CancellationToken ct = default);
        Task<bool> NowPlayingAsync(TrackInfo track, CancellationToken ct = default);
        Task<bool> ScrobbleAsync(ScrobbleEntry entry, CancellationToken ct = default);
    }
}
