using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Spectralis.Core.Capsule;

// Binary layout matches single capsules: [4 magic SPAC][4 version][32 pubkey][64 sig][zip payload].
public sealed class AlbumCapsulePackage : IDisposable
{
    private readonly byte[] _payloadBytes;

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
        _payloadBytes = payloadBytes;
    }

    public string FilePath { get; }
    public AlbumManifest Manifest { get; }
    public byte[] PublicKeyBytes { get; }
    public string Fingerprint { get; }
    public string PayloadSha256 { get; }

    public byte[]? TryReadEntry(string name)
    {
        using var zip = new ZipArchive(new MemoryStream(_payloadBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry(name);
        if (entry is null || entry.Length > CapsuleFormat.MaxEntryBytes)
        {
            return null;
        }

        using var ms = new MemoryStream((int)entry.Length);
        using var stream = entry.Open();
        CapsulePackage.CopyBounded(stream, ms, CapsuleFormat.MaxEntryBytes);
        return ms.ToArray();
    }

    public IReadOnlyList<string> EntryNames()
    {
        using var zip = new ZipArchive(new MemoryStream(_payloadBytes), ZipArchiveMode.Read);
        return zip.Entries.Select(static entry => entry.FullName).ToArray();
    }

    public void ExtractAll(string destinationDirectory)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        using var zip = new ZipArchive(new MemoryStream(_payloadBytes), ZipArchiveMode.Read);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.Length > CapsuleFormat.MaxEntryBytes)
            {
                continue;
            }

            var destinationPath = Path.GetFullPath(Path.Combine(destinationRoot, entry.FullName));
            if (!destinationPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var source = entry.Open();
            using var target = File.Create(destinationPath);
            CapsulePackage.CopyBounded(source, target, CapsuleFormat.MaxEntryBytes);
        }
    }

    public void Dispose()
    {
    }
}

public static class AlbumCapsuleReader
{
    private const int PubKeyOffset = 8;
    private const int SigOffset = 40;
    private const int PayloadOffset = 104; // 4+4+32+64

    public static AlbumCapsulePackage Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);

        if (fs.Length < PayloadOffset + 22)
        {
            throw new InvalidDataException("File is too small to be a .spectral album capsule.");
        }

        if (fs.Length > CapsuleFormat.MaxCapsuleBytes)
        {
            throw new InvalidDataException("Album capsule exceeds the maximum allowed size.");
        }

        var header = new byte[PayloadOffset];
        fs.ReadExactly(header);

        ValidateMagic(header);

        var version = BitConverter.ToInt32(header, 4);
        if (version != AlbumCapsuleFormat.FormatVersion)
        {
            throw new InvalidDataException($"Unsupported album capsule version {version}.");
        }

        var publicKey = header[PubKeyOffset..(PubKeyOffset + 32)];
        var signature = header[SigOffset..(SigOffset + 64)];

        var payloadBytes = new byte[fs.Length - PayloadOffset];
        fs.ReadExactly(payloadBytes);

        VerifySignature(publicKey, signature, payloadBytes);

        var fingerprint = Convert.ToHexString(SHA256.HashData(publicKey)).ToLowerInvariant();
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
        Ed25519PublicKeyParameters pubKeyParams;
        try
        {
            pubKeyParams = new Ed25519PublicKeyParameters(publicKey);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("Album capsule public key is malformed.", ex);
        }

        var verifier = new Ed25519Signer();
        verifier.Init(forSigning: false, pubKeyParams);
        verifier.BlockUpdate(payload, 0, payload.Length);

        if (!verifier.VerifySignature(signature))
        {
            throw new InvalidDataException("Album capsule Ed25519 signature is invalid.");
        }
    }

    private static AlbumManifest ReadManifest(byte[] payloadBytes, string fingerprint)
    {
        using var zip = new ZipArchive(new MemoryStream(payloadBytes), ZipArchiveMode.Read);
        var entry = zip.GetEntry("manifest.json")
            ?? throw new InvalidDataException("Album capsule is missing manifest.json.");

        if (entry.Length > CapsuleFormat.MaxManifestBytes)
        {
            throw new InvalidDataException("manifest.json exceeds the allowed size.");
        }

        AlbumManifest manifest;
        try
        {
            using var manifestBuffer = new MemoryStream();
            using (var stream = entry.Open())
            {
                CapsulePackage.CopyBounded(stream, manifestBuffer, CapsuleFormat.MaxManifestBytes);
            }

            manifestBuffer.Position = 0;
            manifest = JsonSerializer.Deserialize<AlbumManifest>(manifestBuffer)
                ?? throw new InvalidDataException("manifest.json is empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("manifest.json is not valid JSON.", ex);
        }

        if (!string.Equals(manifest.Format, AlbumCapsuleFormat.FormatName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unexpected capsule format '{manifest.Format}'.");
        }

        if (manifest.FormatVersion != AlbumCapsuleFormat.FormatVersion)
        {
            throw new InvalidDataException($"Unsupported album format version {manifest.FormatVersion}.");
        }

        if (!string.Equals(manifest.Signature.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("manifest.json fingerprint does not match the embedded public key.");
        }

        return manifest;
    }
}
