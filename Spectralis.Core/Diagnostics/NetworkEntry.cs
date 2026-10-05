namespace Spectralis.Core.Diagnostics;

public enum NetworkKind
{
    /// <summary>An HTTP request made through one of the app's clients.</summary>
    Http,
    /// <summary>A websocket connection and the frames that cross it.</summary>
    WebSocket,
    /// <summary>A network operation the app can't see inside (for example the updater), tracked around the call.</summary>
    Operation,
}

public enum NetworkState
{
    /// <summary>Sent, no answer yet (or a socket still connecting).</summary>
    Pending,
    /// <summary>Answered or finished normally.</summary>
    Complete,
    /// <summary>Threw, timed out or was cancelled.</summary>
    Failed,
    /// <summary>A websocket that is open right now.</summary>
    Live,
}

/// <summary>One call the app made, as the Network tab in Developer Tools shows it. Secrets are masked before an entry is stored.</summary>
public sealed record NetworkEntry
{
    public long Id { get; init; }
    public NetworkKind Kind { get; init; }
    public NetworkState State { get; init; }

    /// <summary>Which part of the app made the call, for example "shared-play" or "spotify".</summary>
    public string Source { get; init; } = string.Empty;

    public string Method { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public string Host { get; init; } = string.Empty;
    public DateTimeOffset StartedAt { get; init; }
    public double? DurationMs { get; init; }

    public int? StatusCode { get; init; }
    public string? Reason { get; init; }
    public string? Error { get; init; }

    public long? RequestBytes { get; init; }
    public long? ResponseBytes { get; init; }
    public string? ContentType { get; init; }

    public IReadOnlyList<KeyValuePair<string, string>> RequestHeaders { get; init; } = [];
    public IReadOnlyList<KeyValuePair<string, string>> ResponseHeaders { get; init; } = [];

    // websockets
    public int FramesSent { get; init; }
    public int FramesReceived { get; init; }
    public long BytesSent { get; init; }
    public long BytesReceived { get; init; }

    public bool IsError => State == NetworkState.Failed || StatusCode is >= 400;
}
