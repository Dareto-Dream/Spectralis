using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Capsule
{
    public class CapsuleSigner
    {
        public static (string publicKeyBase64, string privateKeyBase64) GenerateKeyPair()
        {
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            return (
                Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()),
                Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey())
            );
        }

        public CapsuleTrust Sign(CapsuleManifest manifest, string privateKeyBase64, string publicKeyBase64)
        {
            var payload = BuildSigningPayload(manifest);
            byte[] data = Encoding.UTF8.GetBytes(payload);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privateKeyBase64), out _);
            byte[] sig = ecdsa.SignData(data, HashAlgorithmName.SHA256);

            return new CapsuleTrust
            {
                PublicKeyBase64 = publicKeyBase64,
                SignatureBase64 = Convert.ToBase64String(sig),
                SignedAt = DateTimeOffset.UtcNow.ToString("o"),
                IsVerified = true
            };
        }

        public bool Verify(CapsuleManifest manifest)
        {
            if (manifest.Trust.PublicKeyBase64 == null || manifest.Trust.SignatureBase64 == null)
                return false;

            try
            {
                var payload = BuildSigningPayload(manifest);
                byte[] data = Encoding.UTF8.GetBytes(payload);

                using var ecdsa = ECDsa.Create();
                ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(manifest.Trust.PublicKeyBase64), out _);
                return ecdsa.VerifyData(data, Convert.FromBase64String(manifest.Trust.SignatureBase64),
                    HashAlgorithmName.SHA256);
            }
            catch { return false; }
        }

        private static string BuildSigningPayload(CapsuleManifest manifest)
        {
            var payload = new
            {
                manifest.Id,
                manifest.Title,
                manifest.Artist,
                manifest.Version,
                manifest.EntryPoint,
                TrackCount = manifest.Tracks.Count
            };
            return JsonSerializer.Serialize(payload);
        }
    }
}
