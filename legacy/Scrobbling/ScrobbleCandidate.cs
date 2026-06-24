namespace Spectralis;

internal sealed record ScrobbleCandidate(
    string Title,
    string Artist,
    string Album,
    double DurationSeconds,
    DateTime StartedAt,
    string FilePath);
