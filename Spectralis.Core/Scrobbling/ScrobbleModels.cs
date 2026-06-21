namespace Spectralis.Core.Scrobbling;

public sealed record ScrobbleCandidate(
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    DateTime StartedAt,
    string FilePath);

public sealed class ScrobbleRecord
{
    public string Title { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Album { get; set; } = "";
    public long Timestamp { get; set; }  // Unix epoch seconds
    public double Duration { get; set; }
}

/// <summary>Account/enable state snapshot handed to the scrobbling service.</summary>
public sealed record ScrobblingConfig(
    bool LastFmEnabled,
    string LastFmApiKey,
    string LastFmApiSecret,
    string LastFmSessionKey,
    bool ListenBrainzEnabled,
    string ListenBrainzToken);
