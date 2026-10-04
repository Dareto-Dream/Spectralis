using System.IO.Compression;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Signers;
using Spectralis.Core.Capsule;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>The verification memory is process-wide, so tests that depend on it must not run side by side.</summary>
[Collection("CapsuleVerificationCache")]
public sealed class CapsuleSignatureVerifierTests : IDisposable
{
    private readonly CapsuleFixture _fixture = new();

    public CapsuleSignatureVerifierTests() => CapsuleSignatureVerifier.ClearCache();

    public void Dispose() => CapsuleSignatureVerifier.ClearCache();

    private (byte[] PublicKey, byte[] Signature, byte[] Payload) Signed(int bytes, int seed = 1)
    {
        var payload = new byte[bytes];
        new Random(seed).NextBytes(payload);
        var signer = new Ed25519Signer();
        signer.Init(true, _fixture.PrivateKey);
        signer.BlockUpdate(payload, 0, payload.Length);
        return (_fixture.PublicKey.GetEncoded(), signer.GenerateSignature(), payload);
    }

    private const int Big = 2 * 1024 * 1024;

    [Fact]
    public void A_valid_signature_verifies_and_a_big_payload_is_remembered()
    {
        var (pk, sig, payload) = Signed(Big);

        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule");
        CapsuleSignatureVerifier.WaitForPending();

        Assert.Equal(1, CapsuleSignatureVerifier.CachedCount);
        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule"); // second time takes the remembered path, still fine
    }

    [Fact]
    public void Small_payloads_are_never_remembered()
    {
        var (pk, sig, payload) = Signed(64 * 1024);

        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule");
        CapsuleSignatureVerifier.WaitForPending();

        Assert.Equal(0, CapsuleSignatureVerifier.CachedCount);
    }

    [Fact]
    public void A_tampered_payload_fails_even_after_the_original_was_remembered()
    {
        var (pk, sig, payload) = Signed(Big);
        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule");
        CapsuleSignatureVerifier.WaitForPending();

        var tampered = (byte[])payload.Clone();
        tampered[Big / 2] ^= 0x01; // same length, same key, same signature, one flipped bit

        var ex = Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(pk, sig, tampered, "Capsule"));
        Assert.Contains("signature is invalid", ex.Message);
    }

    [Fact]
    public void A_flipped_signature_bit_fails_even_after_the_original_was_remembered()
    {
        var (pk, sig, payload) = Signed(Big);
        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule");
        CapsuleSignatureVerifier.WaitForPending();

        var badSig = (byte[])sig.Clone();
        badSig[3] ^= 0x80;

        Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(pk, badSig, payload, "Capsule"));
    }

    [Fact]
    public void A_different_key_cannot_borrow_a_remembered_verification()
    {
        var (pk, sig, payload) = Signed(Big);
        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule");
        CapsuleSignatureVerifier.WaitForPending();

        var other = new CapsuleFixture().PublicKey.GetEncoded();

        Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(other, sig, payload, "Capsule"));
    }

    [Fact]
    public void Failures_are_never_remembered()
    {
        var (pk, sig, payload) = Signed(Big);
        payload[10] ^= 0xFF;

        Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule"));
        CapsuleSignatureVerifier.WaitForPending();

        Assert.Equal(0, CapsuleSignatureVerifier.CachedCount);
        Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule")); // still rejected
    }

    [Fact]
    public void A_malformed_key_and_a_wrong_length_signature_are_rejected_with_clear_messages()
    {
        var (pk, sig, payload) = Signed(1024);

        var keyError = Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(new byte[5], sig, payload, "Album capsule"));
        var sigError = Assert.Throws<InvalidDataException>(() => CapsuleSignatureVerifier.Verify(pk, new byte[10], payload, "Capsule"));

        Assert.Equal("Album capsule public key is malformed.", keyError.Message);
        Assert.Equal("Capsule Ed25519 signature is invalid.", sigError.Message);
    }

    [Fact]
    public void The_memory_is_bounded()
    {
        for (var i = 0; i < 70; i++)
        {
            var (pk, sig, payload) = Signed(Big, seed: i + 1);
            CapsuleSignatureVerifier.Verify(pk, sig, payload, "Capsule", SHA256.HashData(payload)); // digest supplied: no background work
        }

        Assert.Equal(64, CapsuleSignatureVerifier.CachedCount);
    }

    [Fact]
    public void A_supplied_digest_must_really_be_the_payloads()
    {
        // The album reader passes the digest it computed. If it were ever wrong, a remembered entry must not be honoured.
        var (pk, sig, payload) = Signed(Big);
        CapsuleSignatureVerifier.Verify(pk, sig, payload, "Album capsule", SHA256.HashData(payload));

        var tampered = (byte[])payload.Clone();
        tampered[7] ^= 1;

        Assert.Throws<InvalidDataException>(() =>
            CapsuleSignatureVerifier.Verify(pk, sig, tampered, "Album capsule", SHA256.HashData(tampered)));
    }
}

public sealed class CapsulePayloadTests
{
    private static byte[] Zip(IReadOnlyDictionary<string, byte[]> entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var s = zip.CreateEntry(name).Open();
                s.Write(bytes);
            }
        }
        return ms.ToArray();
    }

    private static Dictionary<string, byte[]> Entries(int count, int size)
    {
        var rng = new Random(3);
        var entries = new Dictionary<string, byte[]>();
        for (var i = 0; i < count; i++)
        {
            var bytes = new byte[size];
            rng.NextBytes(bytes);
            entries[$"assets/file-{i:D3}.bin"] = bytes;
        }
        return entries;
    }

    [Fact]
    public void Reads_return_exactly_the_stored_bytes()
    {
        var entries = Entries(5, 4096);
        var payload = new CapsulePayload(Zip(entries));

        foreach (var (name, bytes) in entries)
        {
            Assert.Equal(bytes, payload.TryReadEntry(name, 1 << 20));
        }
    }

    [Fact]
    public void Missing_and_oversized_entries_are_null()
    {
        var payload = new CapsulePayload(Zip(Entries(1, 5000)));

        Assert.Null(payload.TryReadEntry("nope.bin", 1 << 20));
        Assert.Null(payload.TryReadEntry("assets/file-000.bin", 4999));
        Assert.NotNull(payload.TryReadEntry("assets/file-000.bin", 5000));
    }

    [Fact]
    public void An_empty_entry_reads_as_an_empty_array()
    {
        var payload = new CapsulePayload(Zip(new Dictionary<string, byte[]> { ["empty.txt"] = [] }));

        Assert.Empty(payload.TryReadEntry("empty.txt", 10)!);
    }

    [Fact]
    public void Entry_names_are_in_archive_order_and_computed_once()
    {
        var payload = new CapsulePayload(Zip(Entries(4, 10)));

        var first = payload.EntryNames();

        Assert.Equal(["assets/file-000.bin", "assets/file-001.bin", "assets/file-002.bin", "assets/file-003.bin"], first);
        Assert.Same(first, payload.EntryNames());
    }

    [Fact]
    public void Reading_every_entry_allocates_about_the_data_size_not_a_directory_parse_per_read()
    {
        // Regression guard for the old behaviour: a fresh ZipArchive per read (central directory re-parsed
        // every time) plus a second copy of each entry made this ~10x the data size.
        var entries = Entries(300, 32 * 1024);
        var payload = new CapsulePayload(Zip(entries));
        payload.TryReadEntry("assets/file-000.bin", 1 << 20); // warm the pooled archive
        var dataBytes = entries.Values.Sum(b => (long)b.Length);

        var before = GC.GetAllocatedBytesForCurrentThread();
        foreach (var name in entries.Keys) payload.TryReadEntry(name, 1 << 20);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < dataBytes * 1.5, $"allocated {allocated:N0} bytes for {dataBytes:N0} bytes of data");
    }

    [Fact]
    public void Concurrent_readers_each_get_correct_data()
    {
        var entries = Entries(40, 16 * 1024);
        var payload = new CapsulePayload(Zip(entries));
        var names = entries.Keys.ToArray();

        Parallel.For(0, 400, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            var name = names[i % names.Length];
            Assert.Equal(entries[name], payload.TryReadEntry(name, 1 << 20));
        });
    }

    [Fact]
    public void An_action_that_throws_does_not_poison_later_reads()
    {
        var entries = Entries(2, 100);
        var payload = new CapsulePayload(Zip(entries));

        Assert.Throws<InvalidOperationException>(() => payload.Use<int>(_ => throw new InvalidOperationException("boom")));

        Assert.Equal(entries["assets/file-001.bin"], payload.TryReadEntry("assets/file-001.bin", 1 << 20));
    }
}

[Collection("CapsuleVerificationCache")]
public sealed class CapsuleReopenTests : IDisposable
{
    private readonly CapsuleFixture _fixture = new();
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "capsule-reopen-" + Guid.NewGuid().ToString("N"));

    public CapsuleReopenTests()
    {
        Directory.CreateDirectory(_dir);
        CapsuleSignatureVerifier.ClearCache();
    }

    public void Dispose()
    {
        CapsuleSignatureVerifier.ClearCache();
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private string BigCapsule(string name)
    {
        var big = new byte[2 * 1024 * 1024];
        new Random(8).NextBytes(big);
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, _fixture.BuildCapsuleBytes(extraEntries: new Dictionary<string, byte[]> { ["assets/big.bin"] = big }));
        return path;
    }

    [Fact]
    public void Reopening_a_big_capsule_gives_the_same_package_and_entries()
    {
        var path = BigCapsule("big.spectralis");

        var first = CapsuleReader.Read(path);
        CapsuleSignatureVerifier.WaitForPending();
        var second = CapsuleReader.Read(path);

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.Equal(first.TryReadEntry("assets/big.bin"), second.TryReadEntry("assets/big.bin"));
        Assert.Equal(first.EntryNames(), second.EntryNames());
    }

    [Fact]
    public void A_capsule_edited_on_disk_after_loading_is_rejected_on_the_next_load()
    {
        var path = BigCapsule("edited.spectralis");
        CapsuleReader.Read(path);
        CapsuleSignatureVerifier.WaitForPending();

        var bytes = File.ReadAllBytes(path);
        bytes[bytes.Length / 2] ^= 0x01; // inside the zip payload
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => CapsuleReader.Read(path));
    }

    [Fact]
    public void Entries_can_be_read_from_many_threads_at_once()
    {
        var package = CapsuleReader.Read(BigCapsule("threads.spectralis"));
        var expected = package.TryReadEntry("assets/big.bin");

        Parallel.For(0, 24, new ParallelOptions { MaxDegreeOfParallelism = 6 }, _ =>
        {
            Assert.Equal(expected, package.TryReadEntry("assets/big.bin"));
            Assert.NotNull(package.TryReadEntry("manifest.json"));
        });
    }
}
