using System;
using System.IO;
using System.Threading.Tasks;
using Spectralis.Core.Capsule;

namespace Spectralis.App.Services
{
    public class CapsuleV5Service : IDisposable
    {
        private readonly CapsuleV5Reader _reader = new();
        private readonly CapsuleSigner _signer = new();
        private readonly CapsuleTrustStore _trustStore;
        private readonly string _extractRoot;

        public CapsuleManifest? Manifest { get; private set; }
        public string? ExtractedRoot { get; private set; }
        public bool IsVerified { get; private set; }

        public CapsuleV5Service(CapsuleTrustStore trustStore, string extractRoot)
        {
            _trustStore = trustStore;
            _extractRoot = extractRoot;
            Directory.CreateDirectory(extractRoot);
        }

        public async Task<bool> OpenAsync(string capsulePath)
        {
            string name = Path.GetFileNameWithoutExtension(capsulePath);
            string dest = Path.Combine(_extractRoot, name);

            var result = await _reader.ReadAsync(capsulePath, dest);
            if (!result.Success) return false;

            Manifest = result.Manifest;
            ExtractedRoot = result.ExtractedRoot;
            IsVerified = Manifest != null && _signer.Verify(Manifest);
            return true;
        }

        public void Unload()
        {
            Manifest = null;
            ExtractedRoot = null;
            IsVerified = false;
        }

        public void Dispose() => Unload();
    }
}
