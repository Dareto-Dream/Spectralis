using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Audio
{
    public interface ITrackLoader
    {
        IReadOnlyList<string> SupportedExtensions { get; }
        bool CanLoad(string filePath);
        Task<TrackInfo> LoadMetadataAsync(string filePath, CancellationToken ct = default);
    }
}
