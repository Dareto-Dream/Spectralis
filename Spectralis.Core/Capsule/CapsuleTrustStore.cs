using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Spectralis.Core.Capsule
{
    public class CapsuleTrustStore
    {
        private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        private readonly string _path;
        private Dictionary<string, TrustEntry> _entries = new();

        public CapsuleTrustStore(string path) => _path = path;

        public async Task LoadAsync()
        {
            if (!File.Exists(_path)) return;
            try
            {
                string json = await File.ReadAllTextAsync(_path);
                _entries = JsonSerializer.Deserialize<Dictionary<string, TrustEntry>>(json, _json)
                           ?? new();
            }
            catch { _entries = new(); }
        }

        public async Task TrustAsync(string capsuleId, string publicKeyBase64)
        {
            _entries[capsuleId] = new TrustEntry { CapsuleId = capsuleId, PublicKeyBase64 = publicKeyBase64 };
            await SaveAsync();
        }

        public bool IsTrusted(string capsuleId, string publicKeyBase64)
        {
            return _entries.TryGetValue(capsuleId, out var e) && e.PublicKeyBase64 == publicKeyBase64;
        }

        public async Task RevokeAsync(string capsuleId)
        {
            _entries.Remove(capsuleId);
            await SaveAsync();
        }

        private async Task SaveAsync()
        {
            string tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(_entries, _json));
            if (File.Exists(_path)) File.Delete(_path);
            File.Move(tmp, _path);
        }

        public class TrustEntry
        {
            public string CapsuleId { get; set; } = string.Empty;
            public string PublicKeyBase64 { get; set; } = string.Empty;
        }
    }
}
