using System.Threading.Tasks;
using Spectralis.Core.Models;

namespace Spectralis.Core.Tags
{
    public interface ITagEditor
    {
        Task<TrackInfo> ReadTagsAsync(string filePath);
        Task WriteTagsAsync(string filePath, TrackInfo tags);
        Task<bool> CanEditAsync(string filePath);
    }

    public class TagWriteResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string FilePath { get; set; } = string.Empty;

        public static TagWriteResult Ok(string path) => new() { Success = true, FilePath = path };
        public static TagWriteResult Fail(string path, string error) => new() { Success = false, FilePath = path, ErrorMessage = error };
    }
}
