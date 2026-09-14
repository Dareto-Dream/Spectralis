using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.Satellite;

/// <summary>Codec a receiver can ask for. Only <see cref="Pcm"/> is actually encoded by the
/// source today — <see cref="Opus"/> is a declared, negotiable capability so the handshake
/// shape doesn't need to change when real Opus encoding lands, but requesting it currently just
/// gets you Pcm anyway (see SatelliteSourceServer).</summary>
public enum SatelliteCodec
{
    Pcm,
    Opus,
}

/// <summary>What (if anything) a receiver can show — lets the source skip sending FFT data to a
/// receive-only speaker.</summary>
public enum SatelliteDisplay
{
    None,
    Fft,
    Screen,
}

/// <summary>
/// The envelope every control-plane JSON message shares: <c>v</c> (protocol version, for future
/// compatibility checks) and <c>t</c> (message type discriminator) — same shape convention as
/// Shared Play's socket envelope (SharedPlayDefaults.SocketEnvelopeVersion), reused here as a
/// format convention only; Satellite's transport is unrelated (LAN-direct, not backend-relayed).
/// </summary>
public static class SatelliteProtocol
{
    public const int EnvelopeVersion = 1;

    public const string Hello = "hello";
    public const string PairingRequired = "pairingRequired";
    public const string PairRequest = "pairRequest";
    public const string PairResult = "pairResult";
    public const string Capabilities = "capabilities";
    public const string ClockPing = "clockPing";
    public const string ClockPong = "clockPong";
    public const string Error = "error";
}

public sealed class SatelliteHelloMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.Hello;

    /// <summary>Stable per-install identifier the receiver generates once and persists —
    /// mirrors SharedPlayClientIdentity's pattern. This (not the PIN) is what makes a
    /// previously-paired device skip re-pairing on reconnect.</summary>
    public string DeviceId { get; init; } = "";

    public string DisplayName { get; init; } = "";
}

/// <summary>Server -> client, sent right after <c>hello</c> when this device hasn't paired
/// before: "go get the PIN from whoever's looking at the source app, then send pairRequest."
/// The source displays the actual PIN via <c>SatelliteSourceServer.PairingCodeReady</c> — it's
/// deliberately not included in this message, since the whole point is that the human has to
/// see it on the source device, not have it silently handed to any process that connects.</summary>
public sealed class SatellitePairingRequiredMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.PairingRequired;
}

public sealed class SatellitePairRequestMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.PairRequest;
    public string Pin { get; init; } = "";
}

public sealed class SatellitePairResultMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.PairResult;
    public bool Ok { get; init; }
    public string? Reason { get; init; }
}

public sealed class SatelliteCapabilitiesMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.Capabilities;

    public SatelliteCodec Codec { get; init; } = SatelliteCodec.Pcm;

    public SatelliteDisplay Display { get; init; } = SatelliteDisplay.None;
}

/// <summary>Receiver -> source: "what time do you think it is, as of my clock reading t0?"</summary>
public sealed class SatelliteClockPingMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.ClockPing;
    public double T0 { get; init; }
}

/// <summary>Source -> receiver reply: echoes t0, plus the source's own clock readings at
/// receive (t1) and send (t2) time — the receiver combines these with its own t3 (receive time
/// of this message) to compute <see cref="ClockRoundTrip"/>.</summary>
public sealed class SatelliteClockPongMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.ClockPong;
    public double T0 { get; init; }
    public double T1 { get; init; }
    public double T2 { get; init; }
}

public sealed class SatelliteErrorMessage
{
    public int V { get; init; } = SatelliteProtocol.EnvelopeVersion;
    public string T { get; init; } = SatelliteProtocol.Error;
    public string Message { get; init; } = "";
}

/// <summary>
/// Parses the <c>t</c> discriminator out of a control-plane JSON message without committing to
/// a concrete message type — callers switch on the result and deserialize into the matching
/// concrete type. Returns null for malformed input (not an object, or no string <c>t</c>) —
/// treated as "drop this message", never fatal, matching every other untrusted-input boundary
/// in this codebase (WebViewHostService, WasmWorldHost).
/// </summary>
public static class SatelliteMessageReader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string? PeekType(ReadOnlySpan<byte> json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json.ToArray(), new JsonDocumentOptions { MaxDepth = 8 });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return doc.RootElement.TryGetProperty("t", out var tProp) && tProp.ValueKind == JsonValueKind.String
                ? tProp.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static T? Deserialize<T>(ReadOnlySpan<byte> json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static byte[] Serialize<T>(T message) where T : class =>
        JsonSerializer.SerializeToUtf8Bytes(message, Options);
}
