using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Threading.Tasks;
using Spectralis.Core.AlbumWorld;

namespace Spectralis.App.Services
{
    public class AlbumWorldService : IDisposable
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly string _cacheRoot;

        public AlbumWorldManifest? Manifest { get; private set; }
        public AlbumWorldSession? Session { get; private set; }
        public string? ExtractedRoot { get; private set; }

        public AlbumWorldService(string cacheRoot)
        {
            _cacheRoot = cacheRoot;
            Directory.CreateDirectory(cacheRoot);
        }

        public async Task OpenAsync(string spectralPath)
        {
            string worldId = Path.GetFileNameWithoutExtension(spectralPath);
            string cacheDir = Path.Combine(_cacheRoot, worldId);
            bool needsExtract = !Directory.Exists(cacheDir) ||
                File.GetLastWriteTimeUtc(spectralPath) > GetCacheTime(cacheDir);

            if (needsExtract)
            {
                if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
                Directory.CreateDirectory(cacheDir);
                ZipFile.ExtractToDirectory(spectralPath, cacheDir);
                File.SetLastWriteTimeUtc(Path.Combine(cacheDir, ".cache_stamp"), DateTime.UtcNow);
            }

            ExtractedRoot = cacheDir;
            string manifestPath = Path.Combine(cacheDir, "manifest.json");
            if (File.Exists(manifestPath))
            {
                string json = await File.ReadAllTextAsync(manifestPath);
                Manifest = JsonSerializer.Deserialize<AlbumWorldManifest>(json, _json);
            }

            string sessionPath = GetSessionPath(worldId);
            Session = await AlbumWorldSession.LoadAsync(sessionPath)
                ?? new AlbumWorldSession { WorldId = worldId };
        }

        public async Task SaveSessionAsync()
        {
            if (Session == null || Manifest == null) return;
            await Session.SaveAsync(GetSessionPath(Manifest.Id));
        }

        public string? GetWorldHtmlPath()
        {
            if (ExtractedRoot == null || Manifest == null) return null;
            return Path.Combine(ExtractedRoot, Manifest.WorldHtml);
        }

        private string GetSessionPath(string worldId) =>
            Path.Combine(_cacheRoot, worldId + ".session.json");

        private static DateTime GetCacheTime(string cacheDir)
        {
            string stamp = Path.Combine(cacheDir, ".cache_stamp");
            return File.Exists(stamp) ? File.GetLastWriteTimeUtc(stamp) : DateTime.MinValue;
        }

        public void Dispose() { }
    }
}
