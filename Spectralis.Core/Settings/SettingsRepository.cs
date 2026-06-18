using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Settings
{
    public class SettingsRepository
    {
        private readonly string _filePath;
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        public SettingsRepository(string filePath)
        {
            _filePath = filePath;
        }

        public async Task<AppSettings> LoadAsync()
        {
            if (!File.Exists(_filePath)) return new AppSettings();
            try
            {
                string json = await File.ReadAllTextAsync(_filePath);
                return JsonSerializer.Deserialize<AppSettings>(json, _opts) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }

        public async Task SaveAsync(AppSettings settings)
        {
            string dir = Path.GetDirectoryName(_filePath)!;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(settings, _opts);
            await File.WriteAllTextAsync(_filePath, json);
        }

        public async Task<T> GetAsync<T>(string key, T defaultValue)
        {
            var settings = await LoadAsync();
            var prop = typeof(AppSettings).GetProperty(key);
            if (prop == null) return defaultValue;
            object? val = prop.GetValue(settings);
            return val is T typed ? typed : defaultValue;
        }
    }
}
