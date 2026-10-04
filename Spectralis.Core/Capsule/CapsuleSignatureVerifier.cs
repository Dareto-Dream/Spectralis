using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Spectralis.Core.Capsule;

/// <summary>
/// Ed25519 verification of a capsule payload, shared by single and album capsules.
///
/// Two things changed from verifying with BouncyCastle's <c>Ed25519Signer</c>: it buffers a full copy of
/// the message (a 700 MB capsule needed 1.4 GB more memory), so this calls the static verifier directly on
/// the payload array; and re-opening a capsule already verified this session re-ran the whole signature
/// check, so a success is remembered.
///
/// Why remembering is safe: Ed25519 verification is deterministic, so identical (public key, signature,
/// payload) always gives the same answer. A remembered entry is only honoured after the payload's SHA-256
/// matches the digest recorded when it verified, so any byte that differs, in the payload, the signature or
/// the key, is verified from scratch. Only successes are remembered, only in memory (nothing on disk an
/// attacker could plant), and the cache is small and bounded. "Verified on every load" still holds in
/// spirit: every load of bytes we haven't just verified is verified.
///
/// Why it doesn't slow first loads: a cheap check on (key, signature, length) runs first, so a capsule this
/// process has never seen skips hashing entirely, and the digest of a freshly verified payload is recorded
/// on a background thread instead of delaying the caller.
/// </summary>
internal static class CapsuleSignatureVerifier
{
    // Below this, Ed25519 is cheaper than hashing and tracking the result.
    private const int MinCachedPayloadBytes = 1024 * 1024;
    private const int MaxCachedEntries = 64;

    private static readonly object Gate = new();
    // shape ("pubkey:signature:length") -> SHA-256 of the payload that verified under it.
    private static readonly Dictionary<string, string> Verified = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new();
    private static Task _pending = Task.CompletedTask;

    /// <param name="label">"Capsule" or "Album capsule", used in the error messages.</param>
    /// <param name="payloadSha256">The payload's SHA-256 if the caller already has it (saves a pass over the payload).</param>
    public static void Verify(byte[] publicKey, byte[] signature, byte[] payload, string label, byte[]? payloadSha256 = null)
    {
        try
        {
            _ = new Ed25519PublicKeyParameters(publicKey);
        }
        catch (Exception ex)
        {
            throw new InvalidDataException($"{label} public key is malformed.", ex);
        }

        var cacheable = payload.Length >= MinCachedPayloadBytes;
        string? shape = null;
        if (cacheable)
        {
            shape = Shape(publicKey, signature, payload.Length);
            string? remembered;
            lock (Gate)
            {
                Verified.TryGetValue(shape, out remembered);
            }

            // Only now, for a capsule that looks like one we verified before, pay for the digest to be sure.
            if (remembered is not null &&
                string.Equals(remembered, Convert.ToHexString(payloadSha256 ?? SHA256.HashData(payload)), StringComparison.Ordinal))
            {
                return;
            }
        }

        if (signature.Length != Ed25519.SignatureSize ||
            !Ed25519.Verify(signature, 0, publicKey, 0, payload, 0, payload.Length))
        {
            throw new InvalidDataException($"{label} Ed25519 signature is invalid.");
        }

        if (shape is not null)
        {
            Remember(shape, payload, payloadSha256);
        }
    }

    private static string Shape(byte[] publicKey, byte[] signature, int length) =>
        $"{Convert.ToHexString(publicKey)}:{Convert.ToHexString(signature)}:{length}";

    private static void Remember(string shape, byte[] payload, byte[]? payloadSha256)
    {
        if (payloadSha256 is not null)
        {
            Store(shape, Convert.ToHexString(payloadSha256)); // the caller already paid for it
            return;
        }

        // Hashing a big payload takes tens of milliseconds; do it off the caller's path. A re-open that
        // happens before this finishes just verifies normally and queues its own entry.
        var work = Task.Run(() => Store(shape, Convert.ToHexString(SHA256.HashData(payload))));
        lock (Gate)
        {
            _pending = Task.WhenAll(_pending, work);
        }
    }

    private static void Store(string shape, string digest)
    {
        lock (Gate)
        {
            if (!Verified.ContainsKey(shape))
            {
                Order.Enqueue(shape);
            }

            Verified[shape] = digest;
            while (Order.Count > MaxCachedEntries)
            {
                Verified.Remove(Order.Dequeue());
            }
        }
    }

    /// <summary>For tests: waits for background digest recording to finish.</summary>
    internal static void WaitForPending()
    {
        Task pending;
        lock (Gate)
        {
            pending = _pending;
        }

        pending.GetAwaiter().GetResult();
    }

    /// <summary>For tests: forget everything verified so far.</summary>
    internal static void ClearCache()
    {
        WaitForPending();
        lock (Gate)
        {
            Verified.Clear();
            Order.Clear();
        }
    }

    /// <summary>For tests: how many verified payloads are remembered.</summary>
    internal static int CachedCount
    {
        get
        {
            lock (Gate)
            {
                return Verified.Count;
            }
        }
    }
}
