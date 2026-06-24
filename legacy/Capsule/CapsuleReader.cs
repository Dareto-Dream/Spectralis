using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Spectralis;

// Binary layout: [4 magic][4 version][32 pubkey][64 sig][zip payload]
internal sealed class CapsulePackage : IDisposable
{
    private readonly byte[] payloadBytes;

    public string FilePath { get; }
    public CapsuleManifest Manifest { get; }
    public byte[] PublicKeyBytes { get; }
    public string Fingerprint { get; }

    internal CapsulePackage(
        string filePath,
        CapsuleManifest manifest,
        byte[] publicKeyBytes,
        string fingerprint,
        byte[] payloadBytes)
    {
        FilePath = filePath;
        Manifest = manifest;
        PublicKeyBytes = publicKeyBytes;
        Fingerprint = fingerprint;
        this.payloadBytes = payloadBytes;
    }

    public byte[]? TryReadEntry(string name)
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry(name);
        if (entry is null)
            return null;

        using var ms = new MemoryStream((int)entry.Length);
        using var es = entry.Open();
        es.CopyTo(ms);
        return ms.ToArray();
    }

    public IEnumerable<string> EntryNames()
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        return zip.Entries.Select(e => e.FullName).ToArray();
    }

    public void Dispose() { }
}

internal static class CapsuleReader
{
    private const int PubKeyOffset = 8;
    private const int SigOffset = 40;
    private const int PayloadOffset = 104; // 4+4+32+64

    public static CapsulePackage Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (fs.Length < PayloadOffset + 22) // 22 = minimum zip end-of-central-directory
            throw new InvalidDataException("File is too small to be a .spectralis capsule.");

        var header = new byte[PayloadOffset];
        fs.ReadExactly(header);

        ValidateMagic(header);

        var version = BitConverter.ToInt32(header, 4);
        if (version != CapsuleHeader.CurrentVersion)
            throw new InvalidDataException($"Unsupported capsule version {version}.");

        var publicKey = header[PubKeyOffset..(PubKeyOffset + 32)];
        var signature = header[SigOffset..(SigOffset + 64)];

        var payloadBytes = new byte[fs.Length - PayloadOffset];
        fs.ReadExactly(payloadBytes);

        VerifySignature(publicKey, signature, payloadBytes);

        var fingerprintBytes = SHA256.HashData(publicKey);
        var fingerprint = Convert.ToHexString(fingerprintBytes).ToLowerInvariant();

        var manifest = ReadManifest(payloadBytes, fingerprint);

        return new CapsulePackage(path, manifest, publicKey, fingerprint, payloadBytes);
    }

    private static void ValidateMagic(byte[] header)
    {
        if (header[0] != CapsuleHeader.MagicBytes[0] ||
            header[1] != CapsuleHeader.MagicBytes[1] ||
            header[2] != CapsuleHeader.MagicBytes[2] ||
            header[3] != CapsuleHeader.MagicBytes[3])
        {
            throw new InvalidDataException("Not a valid .spectralis capsule (bad magic bytes).");
        }
    }

    private static void VerifySignature(byte[] publicKey, byte[] signature, byte[] payload)
    {
        var pubKeyParams = new Ed25519PublicKeyParameters(publicKey);
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, pubKeyParams);
        verifier.BlockUpdate(payload, 0, payload.Length);

        if (!verifier.VerifySignature(signature))
            throw new InvalidDataException("Capsule Ed25519 signature is invalid.");
    }

    private static CapsuleManifest ReadManifest(byte[] payloadBytes, string fingerprint)
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Capsule is missing manifest.json.");

        CapsuleManifest manifest;
        using (var stream = entry.Open())
            manifest = JsonSerializer.Deserialize<CapsuleManifest>(stream)
                ?? throw new InvalidDataException("manifest.json is empty.");

        if (!string.Equals(manifest.Format, CapsuleFormat.FormatName, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected capsule format '{manifest.Format}'.");

        if (manifest.FormatVersion != CapsuleFormat.FormatVersion)
            throw new InvalidDataException($"Unsupported capsule format version {manifest.FormatVersion}.");

        if (!string.Equals(manifest.Signature.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("manifest.json fingerprint does not match the embedded public key.");

        return manifest;
    }
}
