using System;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using Spectralis.Audio;

namespace Spectralis.Streaming
{
    public interface IStreamingSource : IDisposable
    {
        string Name { get; }
        bool IsAuthenticated { get; }
        Task<StreamingTrack> SearchAsync(string query, CancellationToken ct = default);
        Task<WaveStream> OpenStreamAsync(StreamingTrack track, CancellationToken ct = default);
        Task AuthenticateAsync(CancellationToken ct = default);
    }

    public class StreamingTrack
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public TimeSpan Duration { get; set; }
        public string ThumbnailUrl { get; set; }
        public string Source { get; set; }
        public string StreamUrl { get; set; }
    }
}
