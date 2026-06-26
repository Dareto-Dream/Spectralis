namespace Spectralis;

internal sealed class ScrobblingService
{
    private readonly AppSettings   _settings;
    private readonly ScrobbleQueue _queue = new();

    // ── Track state ───────────────────────────────────────────────────────────

    private ScrobbleCandidate? _current;
    private double             _listenedSeconds;
    private bool               _hasScrobbled;
    private bool               _hasNotifiedNowPlaying;

    public ScrobblingService(AppSettings settings)
    {
        _settings = settings;
        _queue.Load();
    }

    // ── Called by Form1 when a new local track loads ──────────────────────────

    public void NotifyTrackLoaded(string filePath, string title, string artist, string album, double durationSeconds)
    {
        // Flush the previous track first (edge case: very fast skipping)
        _current             = null;
        _listenedSeconds     = 0;
        _hasScrobbled        = false;
        _hasNotifiedNowPlaying = false;

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
            return;

        _current = new ScrobbleCandidate(
            title,
            artist,
            album,
            durationSeconds,
            DateTime.UtcNow,
            filePath);

        if (IsLastFmReady || IsListenBrainzReady)
            _ = SendNowPlayingAsync();
    }

    // ── Called every ~5 s from Form1's timer ─────────────────────────────────

    public void Tick(double positionSeconds, bool isPlaying)
    {
        if (_current is null || _hasScrobbled) return;
        if (!isPlaying) return;

        _listenedSeconds += 5;

        var threshold = Math.Min(_current.DurationSeconds * 0.5, 240.0);
        if (_current.DurationSeconds < 30) return;   // too short to scrobble
        if (_listenedSeconds < threshold)  return;

        _hasScrobbled = true;
        var record = new ScrobbleRecord
        {
            Title     = _current.Title,
            Artist    = _current.Artist,
            Album     = _current.Album,
            Duration  = _current.DurationSeconds,
            Timestamp = new DateTimeOffset(_current.StartedAt).ToUnixTimeSeconds(),
        };

        ScrobbleQueue.AppendHistory(record);
        _queue.Enqueue(record);
        _ = DrainQueueAsync();
    }

    // ── Drain pending queue ───────────────────────────────────────────────────

    public async Task DrainQueueAsync()
    {
        if (_queue.Count == 0) return;
        var batch = _queue.Drain();
        if (batch.Count == 0) return;

        var lfmOk = true;
        var lbzOk = true;

        if (IsLastFmReady)
        {
            var client = new LastFmClient(
                _settings.LastFmApiKey,
                _settings.LastFmApiSecret,
                _settings.LastFmSessionKey);
            lfmOk = await client.ScrobbleAsync(batch);
        }

        if (IsListenBrainzReady)
        {
            var client = new ListenBrainzClient(_settings.ListenBrainzToken);
            lbzOk = await client.SubmitListenAsync(batch);
        }

        if (!lfmOk || !lbzOk)
            _queue.RestoreAll(batch);
    }

    // ── Properties ────────────────────────────────────────────────────────────

    private bool IsLastFmReady =>
        _settings.LastFmEnabled &&
        !string.IsNullOrWhiteSpace(_settings.LastFmSessionKey) &&
        !string.IsNullOrWhiteSpace(_settings.LastFmApiKey) &&
        !string.IsNullOrWhiteSpace(_settings.LastFmApiSecret);

    private bool IsListenBrainzReady =>
        _settings.ListenBrainzEnabled &&
        !string.IsNullOrWhiteSpace(_settings.ListenBrainzToken);

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task SendNowPlayingAsync()
    {
        if (_current is null || _hasNotifiedNowPlaying) return;
        _hasNotifiedNowPlaying = true;

        if (IsLastFmReady)
        {
            var client = new LastFmClient(
                _settings.LastFmApiKey,
                _settings.LastFmApiSecret,
                _settings.LastFmSessionKey);
            await client.UpdateNowPlayingAsync(
                _current.Title, _current.Artist, _current.Album, _current.DurationSeconds);
        }

        if (IsListenBrainzReady)
        {
            var client = new ListenBrainzClient(_settings.ListenBrainzToken);
            await client.SubmitNowPlayingAsync(
                _current.Title, _current.Artist, _current.Album);
        }
    }
}
