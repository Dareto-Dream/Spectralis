using ReactiveUI;
using Spectralis.App.Services;

namespace Spectralis.App.ViewModels;

public sealed partial class NowPlayingViewModel
{
    private readonly QueueAudioCache _queueAudioCache;
    private CancellationTokenSource? _precacheCts;
    private CachedQueueAudio? _upcomingQueueAudio;
    private CachedQueueAudio? _currentQueueAudio;
    private string? _precacheReference;
    private bool _precacheDisposed;
    private Task? _precacheTask;

    public bool PrecacheUpcomingAudio
    {
        get => _settings.PrecacheUpcomingAudio;
        set
        {
            if (_settings.PrecacheUpcomingAudio == value) return;
            _settings.PrecacheUpcomingAudio = value;
            this.RaisePropertyChanged();
            SaveSettings();
            CancelUpcomingAudio();
            RefreshUpcomingAudio();
        }
    }

    private void CancelUpcomingAudio()
    {
        _precacheCts?.Cancel();
        _precacheCts?.Dispose();
        _precacheCts = null;
        _precacheReference = null;
        _engine.CancelPreparedNext();
        _upcomingQueueAudio?.Dispose();
        _upcomingQueueAudio = null;
    }

    private void RefreshUpcomingAudio()
    {
        if (!SessionRoundActive && _currentQueueAudio is { } current && _engine.CurrentTrack?.SourcePath != current.Path)
        {
            current.Dispose();
            _currentQueueAudio = null;
        }
        var reference = !_precacheDisposed && !SessionRoundActive && _engine.IsLoaded ? Queue.PeekNextPath() : null;
        if (reference == _precacheReference && _precacheCts is { IsCancellationRequested: false }) return;
        CancelUpcomingAudio();
        if (reference is null) return;
        var local = File.Exists(reference);
        if (!local && (!PrecacheUpcomingAudio || (DjSourceService.IsSpotify(reference) && !BetaFeatures.SpotifyMatchup))) return;
        _precacheReference = reference;
        _precacheCts = new CancellationTokenSource();
        _queueTrackMetadata.TryGetValue(reference, out var metadata);
        _precacheTask = PrepareUpcomingAudioAsync(new DjSource(reference, Title: metadata.Title, Artist: metadata.Subtitle), local, _precacheCts.Token);
    }

    private async Task PrepareUpcomingAudioAsync(DjSource source, bool local, CancellationToken ct)
    {
        CachedQueueAudio? audio = null;
        try
        {
            if (!local) audio = await _queueAudioCache.AcquireAsync(source, null, ct);
            ct.ThrowIfCancellationRequested();
            if (_precacheDisposed || SessionRoundActive || Queue.PeekNextPath() != source.Reference) return;
            var path = audio?.Path ?? source.Reference;
            await _engine.PrepareNextAsync(path, ct);
            ct.ThrowIfCancellationRequested();
            _upcomingQueueAudio = audio;
            audio = null;
            UpcomingQueueTrackReady?.Invoke(path);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Preloading is optional; foreground playback reports source failures. */ }
        finally { audio?.Dispose(); }
    }

    private async Task<bool> TryLoadCachedQueueAudioAsync(string reference, bool startPlayback)
    {
        if (File.Exists(reference) || (DjSourceService.IsSpotify(reference) && !BetaFeatures.SpotifyMatchup)) return false;
        if (reference == _precacheReference && _precacheCts is { IsCancellationRequested: false } && _precacheTask is { IsCompleted: false } pending)
        {
            IsOpeningRemote = true;
            RemoteStatus = "Preparing queued audio...";
            await pending;
            if (Queue.CurrentPath != reference || _precacheDisposed) return true;
            IsOpeningRemote = false;
            RemoteStatus = string.Empty;
        }
        var audio = _queueAudioCache.TryAcquire(reference);
        if (audio is null) return false;
        // Keep the prepared decoder armed until LoadResolved claims the seamless transition.
        _remoteLoadCts?.Cancel();
        _remoteLoadCts?.Dispose();
        _remoteLoadCts = new CancellationTokenSource();
        var previous = _currentQueueAudio;
        _currentQueueAudio = audio;
        try
        {
            SelectTrackSource();
            LoadError = string.Empty;
            await LoadResolvedAsync(audio.Result, _remoteLoadCts.Token, startPlayback);
            return true;
        }
        finally { previous?.Dispose(); }
    }
}
