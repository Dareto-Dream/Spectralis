using System.IO;
using Avalonia;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

/// <summary>
/// UI Reveal sequence — plays "Delta Changes" on first v5 launch, shows a fake
/// legacy WinForms overlay with kinetic typography, then blasts it away at the
/// "Spectralis changed" moment (t≈173.56 s) to reveal the real v5 UI.
/// Replayable via Help › UI Reveal…
/// </summary>
public partial class MainWindow
{
    // "Spectralis changed" — the white-flash cut-to-black moment in the storyboard.
    // Timestamp from reveal.lrc: [02:53.56] = 173.56 s.
    private const float RevealDropTimeSec = 173.56f;

    private bool _revealActive;
    private bool _revealDropFired;
    private bool _revealVisualsContinuing;
    private DispatcherTimer? _revealPollTimer;

    // ── Initialization ─────────────────────────────────────────────────────

    private void InitializeUiReveal()
    {
        RevealOverlay.SkipRequested += OnRevealSkipRequested;

        // First-launch: auto-play after the window opens (only with no startup args)
        Opened += (_, _) =>
        {
            if (DataContext is not MainWindowViewModel vm) return;
            if (!vm.AppSettings.HasSeenUiReveal && File.Exists(GetRevealAudioPath()))
            {
                // Slight delay so the window is fully painted before the overlay appears
                var delay = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(700),
                    DispatcherPriority.Background,
                    (_, _) => { });
                delay.Tick += (t, _) =>
                {
                    ((DispatcherTimer)t!).Stop();
                    if (!IsVisible || IsVisible == false) return;
                    StartUiReveal();
                };
                delay.Start();
            }
        };
    }

    // ── Audio path ─────────────────────────────────────────────────────────

    private static string GetRevealAudioPath()
    {
        const string rel = "assets/audio/reveal.mp3";
        var fromBase = Path.Combine(AppContext.BaseDirectory, rel);
        return File.Exists(fromBase) ? fromBase
             : Path.Combine(Environment.CurrentDirectory, rel);
    }

    // ── Start ──────────────────────────────────────────────────────────────

    internal async void StartUiReveal()
    {
        if (DataContext is not MainWindowViewModel vm) return;

        var audioPath = GetRevealAudioPath();
        if (!File.Exists(audioPath))
        {
            _ = MessageWindow.ShowAsync(this, "UI Reveal",
                "Reveal audio file not found.\n\nassets/audio/reveal.mp3 must be present in the application folder.");
            return;
        }

        // Reset any in-progress reveal before restarting
        if (_revealActive)
        {
            _revealPollTimer?.Stop();
            _revealPollTimer = null;
            RevealOverlay.Deactivate();
        }

        _revealActive    = true;
        _revealDropFired = false;
        _revealVisualsContinuing = false;

        // Activate overlay BEFORE loading so the user never sees NowPlaying
        // update its UI with the reveal track while the real v5 UI is visible.
        RevealOverlay.Activate();

        // Load through NowPlayingViewModel so the track is fully registered —
        // title, artwork, seek bar, format badge — all ready when the blast
        // reveals the real UI.
        await vm.NowPlaying.LoadTrackAsync(audioPath);
        ApplyRevealTrackDisplay(vm);

        // Guard: reveal may have been cancelled (Escape) during the async I/O
        if (!_revealActive) return;

        // Ensure playback started regardless of the AutoPlayOnOpen setting
        if (!vm.Engine.IsPlaying)
            vm.Engine.Play();

        // Poll timer: updates the overlay position + detects skip/drop
        _revealPollTimer?.Stop();
        _revealPollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Render,
            OnRevealPollTick);
        _revealPollTimer.Start();
    }

    // ── Poll tick ──────────────────────────────────────────────────────────

    private void OnRevealPollTick(object? sender, EventArgs e)
    {
        if (!_revealActive && !_revealVisualsContinuing) return;
        if (DataContext is not MainWindowViewModel vm) return;

        // Detect skip: user pressed Stop/Escape which unloaded the engine
        if (!vm.Engine.IsLoaded)
        {
            if (_revealVisualsContinuing)
                EndRevealContinuation();
            else if (!_revealDropFired)
                EndRevealEarly();
            return;
        }

        var position  = vm.Engine.GetPosition();
        var duration  = vm.Engine.GetLength();
        var toDropSec = Math.Max(0f, RevealDropTimeSec - position);

        RevealOverlay.UpdateFrame(position, duration, toDropSec,
            vm.Engine.GetVisualizerFrame());

        if (!_revealDropFired && position >= RevealDropTimeSec && vm.Engine.IsLoaded)
            TriggerRevealDrop();

        if (_revealVisualsContinuing && duration > 0 && position >= duration - 0.2f)
            EndRevealContinuation();
    }

    // ── Drop trigger ───────────────────────────────────────────────────────

    private void TriggerRevealDrop()
    {
        if (_revealDropFired) return;
        _revealDropFired = true;

        // Window shake: violent 2D nudges timed to the bass hit
        var origin = Position;
        var shakeXY = new (int dx, int dy)[]
        {
            (0, -22), (18, 12), (-24, -8), (14, 18),
            (-10, -12), (8, 6), (-5, -4), (3, 2), (0, 0)
        };
        var step = 0;
        DispatcherTimer? shaker = null;
        shaker = new DispatcherTimer(
            TimeSpan.FromMilliseconds(14),
            DispatcherPriority.Render,
            (_, _) =>
            {
                if (step < shakeXY.Length)
                {
                    var (dx, dy) = shakeXY[step++];
                    Position = new PixelPoint(origin.X + dx, origin.Y + dy);
                }
                else
                {
                    Position = origin;
                    shaker!.Stop();
                }
            });
        shaker.Start();

        RevealOverlay.TriggerBlastAway(FinishReveal);
    }

    // ── Skip ───────────────────────────────────────────────────────────────

    private void OnRevealSkipRequested(object? sender, EventArgs e)
    {
        if (!_revealActive) return;
        EndRevealEarly();
        if (DataContext is MainWindowViewModel vm)
            vm.NowPlaying.ResetPlaybackSession();
    }

    internal void EndRevealEarly()
    {
        if (!_revealActive) return;

        _revealActive    = false;
        _revealVisualsContinuing = false;
        _revealDropFired = true;
        _revealPollTimer?.Stop();
        _revealPollTimer = null;

        RevealOverlay.Deactivate();
        MarkRevealSeen();
    }

    // ── Natural completion ──────────────────────────────────────────────────

    private void FinishReveal()
    {
        _revealActive    = false;
        _revealVisualsContinuing = true;
        RevealOverlay.ContinueAfterHandoff();
        MarkRevealSeen();

        if (DataContext is MainWindowViewModel vm)
        {
            vm.SelectSection(vm.NowPlaying);
            vm.NowPlaying.UseVisualizerSurface();
        }

        // Song keeps playing; the reveal film continues as a non-interactive layer
        // until the rest of the storyboard/audio finishes.
    }

    private void EndRevealContinuation()
    {
        _revealActive = false;
        _revealVisualsContinuing = false;
        _revealDropFired = true;
        _revealPollTimer?.Stop();
        _revealPollTimer = null;
        RevealOverlay.Deactivate();
        MarkRevealSeen();
    }

    private static void ApplyRevealTrackDisplay(MainWindowViewModel vm)
    {
        var logoPath = Path.Combine(AppContext.BaseDirectory, "assets", "audio", "logo.png");
        if (!File.Exists(logoPath))
            logoPath = Path.Combine(Environment.CurrentDirectory, "Assets", "audio", "logo.png");

        var coverArt = File.Exists(logoPath) ? File.ReadAllBytes(logoPath) : null;
        vm.NowPlaying.OverrideCurrentTrackDisplay("spectralis", "deltawave", string.Empty, coverArt);
    }

    // ── Persistence ────────────────────────────────────────────────────────

    private void MarkRevealSeen()
    {
        if (DataContext is not MainWindowViewModel vm) return;
        if (!vm.AppSettings.HasSeenUiReveal)
        {
            vm.AppSettings.HasSeenUiReveal = true;
            AppSettingsStore.Save(vm.AppSettings);
        }
    }
}
