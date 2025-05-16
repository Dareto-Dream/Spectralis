using System;
using System.IO;
using Newtonsoft.Json;

namespace Spectralis.Streaming
{
    public class StreamingAuthStore
    {
        private readonly string _filePath;
        private AuthData _data = new AuthData();

        public StreamingAuthStore(string filePath)
        {
            _filePath = filePath;
            Load();
        }

        public SpotifyTokenSet GetSpotifyTokens() => _data.SpotifyTokens;
        public void SetSpotifyTokens(SpotifyTokenSet tokens)
        {
            _data.SpotifyTokens = tokens;
            Save();
        }

        public string GetSoundCloudClientId() => _data.SoundCloudClientId;
        public void SetSoundCloudClientId(string id) { _data.SoundCloudClientId = id; Save(); }

        public string GetSunoSessionToken() => _data.SunoSessionToken;
        public void SetSunoSessionToken(string token) { _data.SunoSessionToken = token; Save(); }

        public string GetYtDlpPath() => _data.YtDlpPath;
        public void SetYtDlpPath(string path) { _data.YtDlpPath = path; Save(); }

        private void Load()
        {
            if (!File.Exists(_filePath)) return;
            try { _data = JsonConvert.DeserializeObject<AuthData>(File.ReadAllText(_filePath)) ?? new AuthData(); }
            catch { _data = new AuthData(); }
        }

        private void Save()
        {
            File.WriteAllText(_filePath, JsonConvert.SerializeObject(_data, Formatting.Indented));
        }

        private class AuthData
        {
            public SpotifyTokenSet SpotifyTokens { get; set; }
            public string SoundCloudClientId { get; set; }
            public string SunoSessionToken { get; set; }
            public string YtDlpPath { get; set; } = "yt-dlp";
        }
    }
}
