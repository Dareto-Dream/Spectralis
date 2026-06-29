using Avalonia.Threading;
using Spectralis.Core.Audio;
using Spectralis.Core.Integrations;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Services;

/// <summary>
/// Feeds the Discord Rich Presence service from the engine once a second. The
/// service self-deduplicates via presence signatures, silently retries IPC
/// initialization, and stays inert when no application ID is configured.
/// </summary>
public sealed class DiscordPresenceCoordinator : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Func<ListeningActivitySnapshot> _getIdleActivity;
    private readonly DiscordRichPresenceService _service = new();
    private readonly DispatcherTimer _timer;

    /// <summary>Set by Shared Play hosting; appears as the Listen Together button.</summary>
    public string? SharedPlayJoinUrl { get; set; }

    public DiscordPresenceCoordinator(AudioEngine engine, Func<ListeningActivitySnapshot>? getIdleActivity = null)
    {
        _engine = engine;
        _getIdleActivity = getIdleActivity ?? (() => ListeningActivitySnapshot.Empty);
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Push());
        _timer.Start();
    }

    public void SetEnabled(bool enabled) => _service.SetEnabled(enabled);

    private void Push()
    {
        _service.Update(
            _engine.CurrentTrack,
            _engine.IsPlaying,
            TimeSpan.FromSeconds(_engine.GetPosition()),
            TimeSpan.FromSeconds(_engine.GetLength()),
            SharedPlayJoinUrl,
            idleActivity: _getIdleActivity());
    }

    public void Dispose()
    {
        _timer.Stop();
        _service.Dispose();
    }
}
