using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Security;
using Spectralis.Core.Capsule;

namespace Spectralis.Tests.Core;

/// <summary>Builds real signed .spectralis capsules for tests.</summary>
public sealed class CapsuleFixture
{
    public Ed25519PrivateKeyParameters PrivateKey { get; }
    public Ed25519PublicKeyParameters PublicKey { get; }
    public string Fingerprint { get; }

    public CapsuleFixture()
    {
        var generator = new Ed25519KeyPairGenerator();
        generator.Init(new Ed25519KeyGenerationParameters(new SecureRandom()));
        var pair = generator.GenerateKeyPair();
        PrivateKey = (Ed25519PrivateKeyParameters)pair.Private;
        PublicKey = (Ed25519PublicKeyParameters)pair.Public;
        Fingerprint = Convert.ToHexString(SHA256.HashData(PublicKey.GetEncoded())).ToLowerInvariant();
    }

    public byte[] BuildCapsuleBytes(
        IReadOnlyList<string>? capabilities = null,
        string? manifestFingerprintOverride = null,
        IReadOnlyDictionary<string, byte[]>? extraEntries = null)
    {
        var manifest = new CapsuleManifest
        {
            Format = CapsuleFormat.FormatName,
            FormatVersion = CapsuleFormat.FormatVersion,
            Id = "test-capsule",
            Title = "Test Title",
            Artist = "Test Artist",
            Capabilities = capabilities?.ToList() ?? [],
            Signature = new CapsuleSignatureBlock
            {
                KeyId = "test-key",
                Fingerprint = manifestFingerprintOverride ?? Fingerprint,
                Value = "embedded-header",
            },
        };

        using var payloadStream = new MemoryStream();
        using (var zip = new ZipArchive(payloadStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var manifestEntry = zip.CreateEntry("manifest.json");
            using (var stream = manifestEntry.Open())
            {
                stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest));
            }

            var audioEntry = zip.CreateEntry("audio/track.wav");
            using (var stream = audioEntry.Open())
            {
                stream.Write(Encoding.UTF8.GetBytes("fake audio bytes"));
            }

            if (extraEntries is not null)
            {
                foreach (var (name, bytes) in extraEntries)
                {
                    var entry = zip.CreateEntry(name);
                    using var stream = entry.Open();
                    stream.Write(bytes);
                }
            }
        }

        var payload = payloadStream.ToArray();

        var signer = new Ed25519Signer();
        signer.Init(forSigning: true, PrivateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        var signature = signer.GenerateSignature();

        using var file = new MemoryStream();
        file.Write(CapsuleHeader.MagicBytes);
        file.Write(BitConverter.GetBytes(CapsuleHeader.CurrentVersion));
        file.Write(PublicKey.GetEncoded());
        file.Write(signature);
        file.Write(payload);
        return file.ToArray();
    }

    public string WriteCapsule(string directory, string name = "test.spectralis", params string[] capabilities)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, BuildCapsuleBytes(capabilities));
        return path;
    }

    public CreatorKeyMetadata MakeKeyMetadata(
        bool active = true,
        params string[] allowedCapabilities) =>
        new()
        {
            KeyId = "test-key",
            Fingerprint = Fingerprint,
            DisplayName = "Test Creator",
            Status = active ? "active" : "revoked",
            RevokedAtUtc = active ? null : DateTimeOffset.UtcNow,
            AllowedCapabilities = allowedCapabilities.ToList(),
        };
}

/// <summary>HTTP handler that serves canned CDN responses for key fingerprints.</summary>
public sealed class StubCdnHandler : HttpMessageHandler
{
    private readonly Dictionary<string, CreatorKeyMetadata> _keys = new(StringComparer.OrdinalIgnoreCase);

    public int RequestCount { get; private set; }
    public bool FailWithNetworkError { get; set; }

    public void SetKey(CreatorKeyMetadata metadata) => _keys[metadata.Fingerprint] = metadata;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;

        if (FailWithNetworkError)
        {
            throw new HttpRequestException("simulated network failure");
        }

        var fingerprint = Path.GetFileNameWithoutExtension(request.RequestUri!.AbsolutePath);
        if (!_keys.TryGetValue(fingerprint, out var metadata))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json"),
        });
    }
}
