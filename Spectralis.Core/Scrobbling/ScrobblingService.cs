namespace Spectralis.Core.Scrobbling;

/// <summary>
/// Tracks listened time per loaded track, queues scrobbles past the 50%/4-minute
/// threshold, and submits to Last.fm/ListenBrainz with offline retry.
/// </summary>
public sealed class ScrobblingService
{
    private readonly Func<ScrobblingConfig> _getConfig;
    private readonly ScrobbleQueue _queue = new();

    private ScrobbleCandidate? _current;
    private double _listenedSeconds;
    private bool _hasScrobbled;
    private bool _hasNotifiedNowPlaying;

    public ScrobblingService(Func<ScrobblingConfig> getConfig)
    {
        _getConfig = getConfig;
        _queue.Load();
    }

    public int PendingCount => _queue.Count;

    /// <summary>Called when a new local track loads.</summary>
    public void NotifyTrackLoaded(string filePath, string title, string artist, string album, double durationSeconds)
    {
        _current = null;
        _listenedSeconds = 0;
        _hasScrobbled = false;
        _hasNotifiedNowPlaying = false;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            return;
        }

        _current = new ScrobbleCandidate(
            title,
            artist,
            album,
            durationSeconds,
            DateTime.UtcNow,
            filePath);

        var config = _getConfig();
        if (IsLastFmReady(config) || IsListenBrainzReady(config))
        {
            _ = SendNowPlayingAsync();
        }
    }

    /// <summary>Called roughly every 5 seconds while the app runs.</summary>
    public void Tick(double positionSeconds, bool isPlaying)
    {
        if (_current is null || _hasScrobbled || !isPlaying)
        {
            return;
        }

        _listenedSeconds += 5;

        var threshold = Math.Min(_current.DurationSeconds * 0.5, 240.0);
        if (_current.DurationSeconds < 30)
        {
            return;  // too short to scrobble
        }

        if (_listenedSeconds < threshold)
        {
            return;
        }

        _hasScrobbled = true;
        var record = new ScrobbleRecord
        {
            Title = _current.Title,
            Artist = _current.Artist,
            Album = _current.Album,
            Duration = _current.DurationSeconds,
            Timestamp = new DateTimeOffset(_current.StartedAt).ToUnixTimeSeconds(),
        };

        ScrobbleQueue.AppendHistory(record);
        _queue.Enqueue(record);
        _ = DrainQueueAsync();
    }

    public async Task DrainQueueAsync()
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var config = _getConfig();
        if (!IsLastFmReady(config) && !IsListenBrainzReady(config))
        {
            return;  // leave queued until an account is linked
        }

        var batch = _queue.Drain();
        if (batch.Count == 0)
        {
            return;
        }

        var lfmOk = true;
        var lbzOk = true;

        if (IsLastFmReady(config))
        {
            var client = new LastFmClient(config.LastFmApiKey, config.LastFmApiSecret, config.LastFmSessionKey);
            lfmOk = await client.ScrobbleAsync(batch);
        }

        if (IsListenBrainzReady(config))
        {
            var client = new ListenBrainzClient(config.ListenBrainzToken);
            lbzOk = await client.SubmitListenAsync(batch);
        }

        if (!lfmOk || !lbzOk)
        {
            _queue.RestoreAll(batch);
        }
    }

    private static bool IsLastFmReady(ScrobblingConfig config) =>
        config.LastFmEnabled &&
        !string.IsNullOrWhiteSpace(config.LastFmSessionKey) &&
        !string.IsNullOrWhiteSpace(config.LastFmApiKey) &&
        !string.IsNullOrWhiteSpace(config.LastFmApiSecret);

    private static bool IsListenBrainzReady(ScrobblingConfig config) =>
        config.ListenBrainzEnabled &&
        !string.IsNullOrWhiteSpace(config.ListenBrainzToken);

    private async Task SendNowPlayingAsync()
    {
        if (_current is null || _hasNotifiedNowPlaying)
        {
            return;
        }

        _hasNotifiedNowPlaying = true;
        var config = _getConfig();

        if (IsLastFmReady(config))
        {
            var client = new LastFmClient(config.LastFmApiKey, config.LastFmApiSecret, config.LastFmSessionKey);
            await client.UpdateNowPlayingAsync(
                _current.Title, _current.Artist, _current.Album, _current.DurationSeconds);
        }

        if (IsListenBrainzReady(config))
        {
            var client = new ListenBrainzClient(config.ListenBrainzToken);
            await client.SubmitNowPlayingAsync(_current.Title, _current.Artist, _current.Album);
        }
    }
}
