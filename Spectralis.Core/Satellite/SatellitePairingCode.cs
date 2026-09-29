using System.Security.Cryptography;

namespace Spectralis.Core.Satellite;

/// <summary>
/// A 6-digit pairing PIN, mirroring the UX shape of the existing Discord PIN pairing flow
/// (StreamerQueueViewModel) — the source app displays one, the person on the receiver side
/// enters it, one-time explicit-consent handshake, remembered after (see
/// SatelliteSourceServer's paired-device allowlist). Cryptographically random, not
/// sequential/guessable, since this is the only thing standing between "anyone on the LAN" and
/// "an explicitly approved device".
/// </summary>
public static class SatellitePairingCode
{
    public const int Length = 6;

    public static string Generate() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D" + Length);

    /// <summary>Constant-time-ish comparison — a PIN is short-lived and low-entropy anyway, but
    /// there's no reason to make timing attacks free.</summary>
    public static bool Matches(string entered, string expected) =>
        !string.IsNullOrEmpty(entered) &&
        entered.Length == expected.Length &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(entered),
            System.Text.Encoding.ASCII.GetBytes(expected));
}
