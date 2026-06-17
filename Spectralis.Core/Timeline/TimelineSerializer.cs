using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Timeline
{
    public class TimelineSerializer
    {
        private static readonly JsonSerializerOptions _json = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public async Task SaveAsync(ReactiveTimeline timeline, string path)
        {
            string tmp = path + ".tmp";
            string json = JsonSerializer.Serialize(timeline, _json);
            await File.WriteAllTextAsync(tmp, json);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public async Task<ReactiveTimeline?> LoadAsync(string path)
        {
            if (!File.Exists(path)) return null;
            try
            {
                string json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<ReactiveTimeline>(json, _json);
            }
            catch { return null; }
        }
    }
}
