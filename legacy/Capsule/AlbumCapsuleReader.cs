using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Spectralis;

// Binary layout identical to .spectralis: [4 magic SPAC][4 version][32 pubkey][64 sig][zip payload]
internal sealed class AlbumCapsulePackage : IDisposable
{
    private readonly byte[] payloadBytes;

    public string FilePath { get; }
    public AlbumManifest Manifest { get; }
    public byte[] PublicKeyBytes { get; }
    public string Fingerprint { get; }
    public string PayloadSha256 { get; }

    internal AlbumCapsulePackage(
        string filePath,
        AlbumManifest manifest,
        byte[] publicKeyBytes,
        string fingerprint,
        string payloadSha256,
        byte[] payloadBytes)
    {
        FilePath = filePath;
        Manifest = manifest;
        PublicKeyBytes = publicKeyBytes;
        Fingerprint = fingerprint;
        PayloadSha256 = payloadSha256;
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

    public void ExtractAll(string destinationDirectory)
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var destPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!destPath.StartsWith(Path.GetFullPath(destinationDirectory), StringComparison.OrdinalIgnoreCase))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    public void Dispose() { }
}

internal static class AlbumCapsuleReader
{
    private const int PubKeyOffset = 8;
    private const int SigOffset = 40;
    private const int PayloadOffset = 104; // 4+4+32+64

    public static AlbumCapsulePackage Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (fs.Length < PayloadOffset + 22)
            throw new InvalidDataException("File is too small to be a .spectral album capsule.");

        var header = new byte[PayloadOffset];
        fs.ReadExactly(header);

        ValidateMagic(header);

        var version = BitConverter.ToInt32(header, 4);
        if (version != AlbumCapsuleFormat.FormatVersion)
            throw new InvalidDataException($"Unsupported album capsule version {version}.");

        var publicKey = header[PubKeyOffset..(PubKeyOffset + 32)];
        var signature = header[SigOffset..(SigOffset + 64)];

        var payloadBytes = new byte[fs.Length - PayloadOffset];
        fs.ReadExactly(payloadBytes);

        VerifySignature(publicKey, signature, payloadBytes);

        var fingerprintBytes = SHA256.HashData(publicKey);
        var fingerprint = Convert.ToHexString(fingerprintBytes).ToLowerInvariant();
        var payloadSha256 = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();

        var manifest = ReadManifest(payloadBytes, fingerprint);

        return new AlbumCapsulePackage(path, manifest, publicKey, fingerprint, payloadSha256, payloadBytes);
    }

    private static void ValidateMagic(byte[] header)
    {
        if (header[0] != AlbumCapsuleFormat.MagicBytes[0] ||
            header[1] != AlbumCapsuleFormat.MagicBytes[1] ||
            header[2] != AlbumCapsuleFormat.MagicBytes[2] ||
            header[3] != AlbumCapsuleFormat.MagicBytes[3])
        {
            throw new InvalidDataException("Not a valid .spectral album capsule (bad magic bytes).");
        }
    }

    private static void VerifySignature(byte[] publicKey, byte[] signature, byte[] payload)
    {
        var pubKeyParams = new Ed25519PublicKeyParameters(publicKey);
        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, pubKeyParams);
        verifier.BlockUpdate(payload, 0, payload.Length);

        if (!verifier.VerifySignature(signature))
            throw new InvalidDataException("Album capsule Ed25519 signature is invalid.");
    }

    private static AlbumManifest ReadManifest(byte[] payloadBytes, string fingerprint)
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Album capsule is missing manifest.json.");

        AlbumManifest manifest;
        using (var stream = entry.Open())
            manifest = JsonSerializer.Deserialize<AlbumManifest>(stream)
                ?? throw new InvalidDataException("manifest.json is empty.");

        if (!string.Equals(manifest.Format, AlbumCapsuleFormat.FormatName, StringComparison.Ordinal))
            throw new InvalidDataException($"Unexpected capsule format '{manifest.Format}'.");

        if (manifest.FormatVersion != AlbumCapsuleFormat.FormatVersion)
            throw new InvalidDataException($"Unsupported album format version {manifest.FormatVersion}.");

        if (!string.Equals(manifest.Signature.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("manifest.json fingerprint does not match the embedded public key.");

        return manifest;
    }
}
