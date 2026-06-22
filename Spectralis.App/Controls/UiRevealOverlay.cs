// TODO 5.1.0: Remove this file (part of the v5 UI reveal feature)
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.Controls;

/// <summary>
/// Full-window music-video overlay for the v5 reveal. The storyboard is synced
/// to reveal.lrc and uses the bundled reveal imagery as its anchor.
/// </summary>
public sealed class UiRevealOverlay : Control
{
    private const float RevealDropTimeSec = 173.56f;
    private const int FlashEnd = 2;
    private const int GlitchEnd = 8;
    private const int ShatterEnd = 20;

    private static readonly Regex LrcLineRegex = new(@"\[(\d{2}):(\d{2}(?:\.\d+)?)\](.*)", RegexOptions.Compiled);
    private static readonly Regex WordTimeRegex = new(@"<(\d{2}):(\d{2}(?:\.\d+)?)>", RegexOptions.Compiled);

    private readonly Bitmap? _deltaLogo;
    private readonly Bitmap? _oldLogo;
    private readonly Bitmap? _newLogo;
    private readonly List<LyricLine> _lyrics;

    private float _positionSec;
    private float _durationSec;
    private float _toDropSec;
    private VisualizerFrame _vizFrame = VisualizerFrame.Empty;
    private bool _continuationMode;

    private enum BlastPhase { None, Flash, Glitch, Shatter }
    private BlastPhase _blastPhase;
    private int _animFrame;
    private double _shatterAlpha = 1.0;
    private readonly float[] _bandOffsets = new float[64];
    private readonly float[] _bandSpeeds = new float[64];
    private RenderTargetBitmap? _snapshot;
    private Action? _onComplete;
    private DispatcherTimer? _animTimer;

    public event EventHandler? SkipRequested;
    private Rect _skipButtonRect;

    private static readonly Chapter[] Chapters =
    [
        new(0.00f,   "origin",    "#050509", "#10131B", Mood.Nocturne),
        new(7.44f,   "title",     "#050509", "#151A25", Mood.Nocturne),
        new(14.87f,  "memory",    "#08111A", "#202130", Mood.Blueprint),
        new(24.85f,  "old house", "#160C11", "#2A181A", Mood.Horizon),
        new(37.17f,  "build",     "#061111", "#17241E", Mood.Workbench),
        new(51.15f,  "release",   "#08080C", "#1A1018", Mood.Breakout),
        new(69.37f,  "heart",     "#020205", "#130C1D", Mood.Pulse),
        new(92.43f,  "systems",   "#03070C", "#0D1724", Mood.Platforms),
        new(114.03f, "headlight", "#030303", "#151009", Mood.Headlights),
        new(124.78f, "chorus",    "#030205", "#17091F", Mood.Chorus),
        new(134.28f, "skies",     "#071018", "#182925", Mood.Skies),
        new(149.37f, "chapter",   "#090707", "#1C1211", Mood.Chapter),
        new(169.37f, "changed",   "#000000", "#050505", Mood.Final)
    ];

    private static readonly Chapter[] ContinuationChapters =
    [
        new(173.56f, "afterglow",  "#000000", "#050505", Mood.Afterglow),
        new(179.84f, "bridge",     "#030409", "#0B111E", Mood.BridgeHouse),
        new(195.51f, "final",      "#05070A", "#11151A", Mood.FinalNetwork),
        new(224.79f, "prototype",  "#050505", "#101018", Mood.PrototypeOutro)
    ];

    private static readonly Color Ink = Color.Parse("#F4F5F7");
    private static readonly Color MutedInk = Color.Parse("#A8ADB8");
    private static readonly Color DimInk = Color.Parse("#626A76");
    private static readonly Color Signal = Color.Parse("#FF4E1A");
    private static readonly Color SignalGold = Color.Parse("#FFB84D");
    private static readonly Color Cyan = Color.Parse("#23D7FF");
    private static readonly Color Violet = Color.Parse("#9B6BF2");
    private static readonly Color Green = Color.Parse("#4DFFB0");

    public UiRevealOverlay()
    {
        IsHitTestVisible = true;
        IsVisible = false;

        _deltaLogo = LoadBitmap("delta.png");
        _oldLogo = LoadBitmap("old_logo.png");
        _newLogo = LoadBitmap("logo.png");
        _lyrics = LoadLyrics();
    }

    public void Activate()
    {
        _blastPhase = BlastPhase.None;
        _continuationMode = false;
        _positionSec = 0;
        _durationSec = 0;
        _toDropSec = 0;
        _vizFrame = VisualizerFrame.Empty;
        IsHitTestVisible = true;
        IsVisible = true;
        InvalidateVisual();
    }

    public void UpdateFrame(float positionSec, float durationSec, float toDropSec, VisualizerFrame frame)
    {
        _positionSec = positionSec;
        _durationSec = durationSec;
        _toDropSec = toDropSec;
        _vizFrame = frame;
        if (_blastPhase == BlastPhase.None || _continuationMode)
            InvalidateVisual();
    }

    public void ContinueAfterHandoff()
    {
        _continuationMode = true;
        _blastPhase = BlastPhase.None;
        IsHitTestVisible = false;
        IsVisible = true;
        _skipButtonRect = default;
        InvalidateVisual();
    }

    public void TriggerBlastAway(Action onComplete)
    {
        if (_blastPhase != BlastPhase.None)
            return;

        _onComplete = onComplete;
        _snapshot?.Dispose();
        _snapshot = TryTakeSnapshot();

        var rng = new Random(42);
        for (var i = 0; i < _bandSpeeds.Length; i++)
        {
            _bandSpeeds[i] = (float)(80.0 + rng.NextDouble() * 220.0);
            _bandOffsets[i] = 0;
        }

        _blastPhase = BlastPhase.Flash;
        _animFrame = 0;
        _shatterAlpha = 1.0;

        _animTimer?.Stop();
        _animTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnAnimTick);
        _animTimer.Start();
    }

    public void Deactivate()
    {
        _animTimer?.Stop();
        _animTimer = null;
        _blastPhase = BlastPhase.None;
        _continuationMode = false;
        IsHitTestVisible = true;
        IsVisible = false;
        _snapshot?.Dispose();
        _snapshot = null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (_skipButtonRect.Contains(e.GetPosition(this)))
        {
            e.Handled = true;
            SkipRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        _animFrame++;
        InvalidateVisual();

        if (_animFrame == FlashEnd)
            _blastPhase = BlastPhase.Glitch;
        if (_animFrame == GlitchEnd)
            _blastPhase = BlastPhase.Shatter;

        if (_blastPhase == BlastPhase.Shatter)
        {
            var sf = _animFrame - GlitchEnd;
            for (var i = 0; i < _bandOffsets.Length; i++)
            {
                var direction = i % 2 == 0 ? 1f : -1f;
                var surge = i % 5 == 0 ? 1.45f : 1f;
                _bandOffsets[i] = _bandSpeeds[i] * direction * surge * sf * sf * 0.36f;
            }

            _shatterAlpha = Math.Max(0, 1.0 - Math.Pow(sf / 12.0, 0.58));
        }

        if (_animFrame >= ShatterEnd)
        {
            _animTimer?.Stop();
            _animTimer = null;
            _blastPhase = BlastPhase.None;
            _continuationMode = true;
            IsHitTestVisible = false;
            IsVisible = true;
            _snapshot?.Dispose();
            _snapshot = null;
            var cb = _onComplete;
            _onComplete = null;
            Dispatcher.UIThread.Post(() => cb?.Invoke());
        }
    }

    public override void Render(DrawingContext ctx)
    {
        if (!IsVisible)
            return;

        switch (_blastPhase)
        {
            case BlastPhase.None:
                if (_continuationMode)
                    DrawContinuation(ctx);
                else
                {
                    DrawReveal(ctx);
                    DrawSkipButton(ctx);
                }
                return;
            case BlastPhase.Flash:
                DrawReveal(ctx);
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)), new Rect(Bounds.Size));
                return;
            case BlastPhase.Glitch:
                RenderGlitch(ctx);
                return;
            case BlastPhase.Shatter:
                RenderShatter(ctx);
                return;
        }
    }

    private RenderTargetBitmap? TryTakeSnapshot()
    {
        try
        {
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return null;

            var sc = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
            var bmp = new RenderTargetBitmap(
                new PixelSize((int)(Bounds.Width * sc), (int)(Bounds.Height * sc)),
                new Vector(96 * sc, 96 * sc));
            bmp.Render(this);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void DrawReveal(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        var chapterIndex = GetChapterIndex(_positionSec);
        var chapter = Chapters[chapterIndex];
        var nextStart = chapterIndex < Chapters.Length - 1 ? Chapters[chapterIndex + 1].Start : RevealDropTimeSec;
        var p = Ease(Progress(_positionSec, chapter.Start, nextStart));
        var rms = Clamp01(_vizFrame.RmsFast * 2.6);
        var peak = Clamp01(_vizFrame.PeakLevel * 1.4);
        var spec = _vizFrame.Spectrum.Length > 0 ? _vizFrame.Spectrum : Array.Empty<float>();
        var wave = _vizFrame.WaveformL.Length > 0 ? _vizFrame.WaveformL : Array.Empty<float>();

        DrawAtmosphere(ctx, w, h, chapter, p, rms, peak);
        DrawBackgroundWatermark(ctx, w, h, chapter.Mood, rms);
        DrawScene(ctx, w, h, chapter.Mood, p, rms, peak, spec, wave);
        DrawGlobalAudio(ctx, w, h, spec, wave, rms, peak);
        DrawLyrics(ctx, w, h);
        DrawTimeline(ctx, w, h);

        var xfade = nextStart - _positionSec;
        if (chapterIndex < Chapters.Length - 1 && xfade is > 0 and < 0.65f)
        {
            using (ctx.PushOpacity(1.0 - xfade / 0.65))
            {
                var next = Chapters[chapterIndex + 1];
                DrawAtmosphere(ctx, w, h, next, 0, rms, peak);
                DrawScene(ctx, w, h, next.Mood, 0, rms, peak, spec, wave);
            }
        }
    }

    private void DrawContinuation(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
            return;

        var chapterIndex = GetContinuationChapterIndex(_positionSec);
        var chapter = ContinuationChapters[chapterIndex];
        var end = chapterIndex < ContinuationChapters.Length - 1
            ? ContinuationChapters[chapterIndex + 1].Start
            : Math.Max(_durationSec, 268.48f);
        var p = Ease(Progress(_positionSec, chapter.Start, end));
        var rms = Clamp01(_vizFrame.RmsFast * 2.6);
        var peak = Clamp01(_vizFrame.PeakLevel * 1.4);
        var spec = _vizFrame.Spectrum.Length > 0 ? _vizFrame.Spectrum : Array.Empty<float>();
        var wave = _vizFrame.WaveformL.Length > 0 ? _vizFrame.WaveformL : Array.Empty<float>();

        using (ctx.PushOpacity(0.62))
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(88, 0, 0, 0)), new Rect(0, 0, w, h));
            DrawContinuationAtmosphere(ctx, w, h, chapter, p, rms, peak);
            DrawContinuationScene(ctx, w, h, chapter.Mood, p, rms, peak, spec, wave);
            DrawGlobalAudio(ctx, w, h, spec, wave, rms, peak);
        }

        DrawLyrics(ctx, w, h);
        DrawContinuationTimeline(ctx, w, h);
    }

    private static int GetContinuationChapterIndex(float time)
    {
        for (var i = ContinuationChapters.Length - 1; i >= 0; i--)
        {
            if (time >= ContinuationChapters[i].Start)
                return i;
        }

        return 0;
    }

    private static void DrawContinuationAtmosphere(DrawingContext ctx, double w, double h, Chapter chapter, double p, double rms, double peak)
    {
        var top = Color.Parse(chapter.Top);
        var bottom = Color.Parse(chapter.Bottom);
        var tint = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops
            {
                new(Color.FromArgb(150, top.R, top.G, top.B), 0),
                new(Color.FromArgb(150, bottom.R, bottom.G, bottom.B), 1)
            }
        };
        ctx.FillRectangle(tint, new Rect(0, 0, w, h));
        DrawPerspectiveGrid(ctx, w, h, Color.FromArgb((byte)(18 + rms * 35 + peak * 20), 255, 255, 255));
        DrawFilmLines(ctx, w, h, (byte)(10 + 15 * rms));
    }

    private void DrawContinuationScene(
        DrawingContext ctx,
        double w,
        double h,
        Mood mood,
        double p,
        double rms,
        double peak,
        float[] spec,
        float[] wave)
    {
        switch (mood)
        {
            case Mood.Afterglow:
                DrawAfterglowContinuation(ctx, w, h, p, rms, peak, wave);
                break;
            case Mood.BridgeHouse:
                DrawBridgeHouseContinuation(ctx, w, h, p, rms);
                break;
            case Mood.FinalNetwork:
                DrawFinalNetworkContinuation(ctx, w, h, p, rms, peak, spec, wave);
                break;
            case Mood.PrototypeOutro:
                DrawPrototypeOutroContinuation(ctx, w, h, p, rms, peak, wave);
                break;
        }
    }

    private void DrawAfterglowContinuation(DrawingContext ctx, double w, double h, double p, double rms, double peak, float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var settle = Math.Clamp(1 - p, 0, 1);

        DrawRadialBurst(ctx, cx, cy, unit * (0.55 - p * 0.18), 24, Color.FromRgb(255, 255, 255), 0.16 * settle + peak * 0.10);
        DrawImageCentered(ctx, _newLogo, new Point(cx, cy), unit * (0.27 + rms * 0.04), unit * (0.27 + rms * 0.04));
        DrawLogoBurstShards(ctx, new Point(cx, cy), unit * 0.24, settle, SignalGold, Cyan);
        DrawWaveformLine(ctx, wave, cx, h * 0.76, w * 0.72, unit * (0.055 + rms * 0.04), Cyan, 0.72);
    }

    private void DrawBridgeHouseContinuation(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var cube = unit * (0.24 + p * 0.08);
        var roofOpen = Math.Clamp((p - 0.55) / 0.3, 0, 1);

        DrawStars(ctx, w, h, 95, 91, 0.38 + roofOpen * 0.45);
        DrawIsoFrame(ctx, new Point(cx, cy - unit * 0.02), cube, Green, 0.74);
        DrawHouseMark(ctx, new Point(cx, cy - unit * 0.02), cube * 0.56, SignalGold, 0.86);
        DrawWindowReplayTiles(ctx, cx, cy - unit * 0.02, cube, p);

        if (roofOpen > 0)
        {
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(210 * roofOpen), 255, 245, 190)), 2.4);
            ctx.DrawLine(pen, new Point(cx - cube * 0.28, cy - cube * 0.36), new Point(cx - cube * (0.48 + roofOpen * 0.28), cy - cube * (0.68 + roofOpen * 0.22)));
            ctx.DrawLine(pen, new Point(cx + cube * 0.28, cy - cube * 0.36), new Point(cx + cube * (0.48 + roofOpen * 0.28), cy - cube * (0.68 + roofOpen * 0.22)));
            DrawRadialBurst(ctx, cx, cy - cube * 0.62, cube * (0.26 + roofOpen * 0.35), 16, Color.FromRgb(255, 255, 255), 0.18 * roofOpen);
        }
    }

    private void DrawFinalNetworkContinuation(DrawingContext ctx, double w, double h, double p, double rms, double peak, float[] spec, float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var center = new Point(cx, cy - unit * 0.04);

        DrawRoad(ctx, cx, h * 0.70, w, h, Color.FromArgb(120, 255, 190, 110));
        DrawNetworkBranches(ctx, w, h, p);
        DrawDeviceSunriseStrip(ctx, w, h, p, wave);
        DrawSpectrumRing(ctx, center, unit * (0.27 + rms * 0.04), spec, 0.36 + rms * 0.18);

        using (ctx.PushOpacity(1 - Math.Clamp((p - 0.66) / 0.24, 0, 1) * 0.7))
        {
            DrawImageCentered(ctx, _oldLogo, center, unit * (0.28 + rms * 0.04), unit * (0.28 + rms * 0.04));
            DrawCracks(ctx, center.X, center.Y, unit * 0.18, Math.Clamp(0.65 + p * 0.45, 0, 1), rms);
        }

        using (ctx.PushOpacity(Math.Clamp((p - 0.52) / 0.35, 0, 1)))
            DrawImageCentered(ctx, _newLogo, center, unit * (0.30 + peak * 0.04), unit * (0.30 + peak * 0.04));

        DrawOrbitingUiPanels(ctx, center, unit * (0.34 + rms * 0.06), p, peak);
        DrawLogoBurstShards(ctx, center, unit * 0.24, Math.Clamp((p - 0.70) / 0.25, 0, 1), SignalGold, Cyan);
        DrawWaveformLine(ctx, wave, cx, h * 0.79, w * 0.78, unit * (0.06 + rms * 0.04), Color.FromRgb(255, 255, 255), 0.72);
    }

    private void DrawPrototypeOutroContinuation(DrawingContext ctx, double w, double h, double p, double rms, double peak, float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var oldCenter = new Point(cx - unit * 0.20 + unit * 0.14 * p, cy - unit * 0.04 + unit * 0.12 * p);
        var newCenter = new Point(cx + unit * 0.20 - unit * 0.12 * p, cy - unit * 0.04);
        var foundation = new Rect(cx - unit * 0.34, cy + unit * (0.16 + p * 0.10), unit * 0.68, unit * 0.13);

        DrawBlueprintGrid(ctx, w, h, 0.18 + rms * 0.15);
        using (ctx.PushOpacity(1 - p * 0.65))
            DrawImageCentered(ctx, _oldLogo, oldCenter, unit * 0.23, unit * 0.23);
        DrawWireframeFoundation(ctx, foundation, p, Cyan);
        DrawImageCentered(ctx, _newLogo, newCenter, unit * (0.30 + rms * 0.04 + peak * 0.02), unit * (0.30 + rms * 0.04 + peak * 0.02));
        DrawClosingBlueprintBook(ctx, new Rect(cx - unit * 0.42, cy - unit * 0.31, unit * 0.84, unit * 0.62), Math.Clamp((p - 0.68) / 0.28, 0, 1));
        DrawWaveformLine(ctx, wave, cx, h * 0.78, w * 0.64, unit * (0.035 + rms * 0.03), SignalGold, 0.58);
    }

    private static int GetChapterIndex(float time)
    {
        for (var i = Chapters.Length - 1; i >= 0; i--)
        {
            if (time >= Chapters[i].Start)
                return i;
        }

        return 0;
    }

    private void DrawAtmosphere(DrawingContext ctx, double w, double h, Chapter chapter, double p, double rms, double peak)
    {
        DrawGradient(ctx, w, h, Color.Parse(chapter.Top), Color.Parse(chapter.Bottom));

        var pulse = 0.5 + rms * 0.5 + peak * 0.2;
        DrawPerspectiveGrid(ctx, w, h, Color.FromArgb((byte)(22 + 45 * pulse), 255, 255, 255));
        DrawVignette(ctx, w, h);
        DrawFilmLines(ctx, w, h, (byte)(16 + 24 * rms));

        if (chapter.Mood is Mood.Blueprint or Mood.Workbench or Mood.Platforms)
            DrawBlueprintGrid(ctx, w, h, 0.15 + rms * 0.15);

        if (chapter.Mood is Mood.Chorus or Mood.Final)
            DrawRadialBurst(ctx, w / 2, h / 2, Math.Min(w, h) * (0.32 + p * 0.18), 20, Signal, 0.13 + rms * 0.25);
    }

    private void DrawScene(
        DrawingContext ctx,
        double w,
        double h,
        Mood mood,
        double p,
        double rms,
        double peak,
        float[] spec,
        float[] wave)
    {
        switch (mood)
        {
            case Mood.Nocturne:
                DrawLogoConstellation(ctx, w, h, p, rms);
                break;
            case Mood.Blueprint:
                DrawBlueprintMemory(ctx, w, h, p, rms);
                break;
            case Mood.Horizon:
                DrawOldHouseHorizon(ctx, w, h, p, rms);
                break;
            case Mood.Workbench:
                DrawWorkbench(ctx, w, h, p, rms);
                break;
            case Mood.Breakout:
                DrawBreakout(ctx, w, h, p, rms, peak);
                break;
            case Mood.Pulse:
                DrawHeartPulse(ctx, w, h, p, rms, peak, spec, wave);
                break;
            case Mood.Platforms:
                DrawPlatformSystems(ctx, w, h, p, rms, wave);
                break;
            case Mood.Headlights:
                DrawHeadlights(ctx, w, h, p, rms);
                break;
            case Mood.Chorus:
                DrawNewFrameChorus(ctx, w, h, p, rms, peak, spec, wave);
                break;
            case Mood.Skies:
                DrawSkies(ctx, w, h, p, rms, spec);
                break;
            case Mood.Chapter:
                DrawChapterTurn(ctx, w, h, p, rms);
                break;
            case Mood.Final:
                DrawFinalBuild(ctx, w, h, p, rms, peak, wave);
                break;
        }
    }

    private void DrawBackgroundWatermark(DrawingContext ctx, double w, double h, Mood mood, double rms)
    {
        if (mood is Mood.Nocturne or Mood.Final)
            return;

        var unit = Math.Min(w, h);
        using (ctx.PushOpacity(0.025 + rms * 0.025))
            DrawImageCentered(ctx, _newLogo, new Point(w / 2, h / 2), unit * 1.12, unit * 1.12);
    }

    private void DrawLogoConstellation(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var intro = _positionSec < 7.44f ? Ease(Progress(_positionSec, 0, 7.1f)) : 1.0;

        DrawStars(ctx, w, h, 70, 17, 0.35 + rms * 0.4);
        DrawRadialBurst(ctx, cx, cy, unit * (0.24 + rms * 0.08), 18, Violet, 0.16 + rms * 0.14);

        using (ctx.PushOpacity(intro))
        {
            var drift = Math.Sin(_positionSec * 0.55) * unit * 0.012;
            DrawGlowDisc(ctx, new Point(cx, cy + drift), unit * (0.17 + rms * 0.02), Violet, 0.18 + rms * 0.12);
            DrawImageCentered(ctx, _deltaLogo, new Point(cx, cy + drift), unit * 0.34, unit * 0.34);
        }

        if (_positionSec >= 7.44f)
        {
            var titleP = Ease(Progress(_positionSec, 7.44f, 12.5f));
            var title = MakeText("Delta Changes", FitSize("Delta Changes", w * 0.75, unit * 0.086, FontWeight.Bold),
                new SolidColorBrush(Ink), FontWeight.Bold, "Segoe UI Variable Display");
            using (ctx.PushOpacity(titleP))
                ctx.DrawText(title, new Point(cx - title.Width / 2, h * 0.70 - title.Height / 2));
        }
    }

    private void DrawBlueprintMemory(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var panel = new Rect(w * 0.18, h * 0.19, w * 0.64, h * 0.48);

        DrawBlueprintSheet(ctx, panel, p);
        using (ctx.PushOpacity(0.18 + p * 0.45))
            DrawImageCentered(ctx, _oldLogo, panel.Center, unit * 0.34, unit * 0.34);

        var pen = new Pen(new SolidColorBrush(Color.FromArgb(180, 103, 214, 255)), 2);
        var x = panel.X + panel.Width * 0.16;
        for (var i = 0; i < 5; i++)
        {
            var y = panel.Y + panel.Height * (0.18 + i * 0.12);
            var len = panel.Width * (0.32 + 0.11 * ((i + 2) % 3));
            var draw = Math.Clamp((p * 6) - i, 0, 1);
            ctx.DrawLine(pen, new Point(x, y), new Point(x + len * draw, y));
        }

        if (_positionSec >= 19.55f)
            DrawErrorNeedles(ctx, cx, cy, unit, Progress(_positionSec, 19.55f, 24.85f), rms);
    }

    private void DrawOldHouseHorizon(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var unit = Math.Min(w, h);
        var horizon = h * (0.62 - p * 0.12);

        DrawSunsetBands(ctx, w, h, p);
        DrawWindowFrame(ctx, new Rect(cx - unit * 0.18, h * 0.18, unit * 0.36, unit * 0.32),
            Color.FromArgb(210, 255, 190, 120), 3);
        DrawRoad(ctx, cx, horizon, w, h, Color.FromArgb(130, 255, 210, 130));

        var dreamAlpha = Math.Clamp((p - 0.45) / 0.4, 0, 1);
        using (ctx.PushOpacity(dreamAlpha))
        {
            DrawGlowDisc(ctx, new Point(cx + unit * 0.25, h * 0.28), unit * 0.08, Cyan, 0.18 + rms * 0.16);
            DrawPlayGlyph(ctx, new Point(cx + unit * 0.25, h * 0.28), unit * 0.055, Ink, 2.5);
        }
    }

    private void DrawWorkbench(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var cube = unit * 0.24;
        var build = Math.Clamp(p * 1.2, 0, 1);

        DrawIsoFrame(ctx, new Point(cx, cy - unit * 0.06), cube, Green, 0.75 * build);
        DrawHouseMark(ctx, new Point(cx, cy - unit * 0.06), cube * 0.58, SignalGold, 0.78 * build);
        DrawCodeRails(ctx, w, h, p, rms);

        var leave = Math.Clamp((p - 0.68) / 0.28, 0, 1);
        if (leave > 0)
        {
            var person = new Point(cx + cube * (0.54 + leave * 1.05), cy + cube * 0.08);
            DrawPerson(ctx, person, unit * 0.09, Color.FromArgb((byte)(240 * leave), 190, 255, 225), 2.3);
        }
    }

    private void DrawBreakout(DrawingContext ctx, double w, double h, double p, double rms, double peak)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);

        if (p < 0.34)
        {
            var wallP = p / 0.34;
            var wall = new Rect(cx - unit * 0.13, h * (0.82 - wallP * 0.45), unit * 0.26, h * 0.52);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(210, 46, 43, 43)), wall);
            DrawBrickLines(ctx, wall, Color.FromArgb(90, 255, 255, 255));
            DrawPerson(ctx, new Point(cx, wall.Y - unit * 0.03), unit * 0.07, Color.FromArgb(200, 170, 195, 255), 2);
        }
        else if (p < 0.66)
        {
            DrawRoad(ctx, cx, h * 0.52, w, h, Color.FromArgb(150, 255, 180, 90));
        }
        else
        {
            var breakP = (p - 0.66) / 0.34;
            DrawChain(ctx, cx, cy, unit * 0.58, breakP, Color.FromArgb(210, 190, 198, 220));
            DrawRadialBurst(ctx, cx, cy, unit * (0.05 + breakP * 0.3), 12, SignalGold, 0.14 + peak * 0.3 + rms * 0.16);
        }
    }

    private void DrawHeartPulse(
        DrawingContext ctx,
        double w,
        double h,
        double p,
        double rms,
        double peak,
        float[] spec,
        float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var logoSize = unit * (0.36 + rms * 0.05 + peak * 0.03);

        DrawGlowDisc(ctx, new Point(cx, cy - unit * 0.04), unit * (0.25 + rms * 0.04), Violet, 0.18 + rms * 0.16);
        DrawImageCentered(ctx, _oldLogo, new Point(cx, cy - unit * 0.04), logoSize, logoSize);
        DrawSpectrumRing(ctx, new Point(cx, cy - unit * 0.04), unit * 0.28, spec, 0.36 + rms * 0.22);
        DrawWaveformLine(ctx, wave, cx, h * 0.73, w * 0.72, unit * (0.05 + rms * 0.04), Color.Parse("#CF8BFF"), 0.75);
    }

    private void DrawPlatformSystems(DrawingContext ctx, double w, double h, double p, double rms, float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var monitors = new[]
        {
            (Center: new Point(cx - unit * 0.32, cy), Color: Color.Parse("#3AA8FF"), Label: "WIN"),
            (Center: new Point(cx, cy - unit * 0.04), Color: Color.Parse("#EBEDF0"), Label: "MAC"),
            (Center: new Point(cx + unit * 0.32, cy), Color: Color.Parse("#FFB04A"), Label: "LINUX")
        };

        DrawRoad(ctx, cx, h * 0.72, w, h, Color.FromArgb(70, 100, 170, 255));

        for (var i = 0; i < monitors.Length; i++)
        {
            var reveal = Math.Clamp((p * 4) - i * 0.55, 0, 1);
            using (ctx.PushOpacity(reveal))
            {
                var rect = new Rect(monitors[i].Center.X - unit * 0.13, monitors[i].Center.Y - unit * 0.075, unit * 0.26, unit * 0.15);
                DrawMonitor(ctx, rect, monitors[i].Color, 2);
                DrawWaveformLine(ctx, wave, rect.Center.X, rect.Center.Y, rect.Width * 0.68, rect.Height * 0.24,
                    monitors[i].Color, 0.65 + rms * 0.2);
                var label = MakeText(monitors[i].Label, unit * 0.018, new SolidColorBrush(monitors[i].Color), FontWeight.Bold, "Cascadia Mono");
                ctx.DrawText(label, new Point(rect.Center.X - label.Width / 2, rect.Bottom + 8));
            }
        }

        var doorP = Math.Clamp((p - 0.68) / 0.25, 0, 1);
        if (doorP > 0)
            DrawDoor(ctx, new Rect(cx + unit * 0.46, h * 0.30, unit * 0.16, unit * 0.30), doorP);
    }

    private void DrawHeadlights(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var unit = Math.Min(w, h);
        DrawRoad(ctx, cx, h * 0.68, w, h, Color.FromArgb(120, 255, 225, 170));

        var vehicleP = Math.Clamp(p / 0.72, 0, 1);
        var y = h * (0.66 - vehicleP * 0.22);
        var spread = unit * (0.08 + vehicleP * 0.18);
        var radius = unit * (0.032 + vehicleP * 0.06);
        DrawHeadlightCone(ctx, new Point(cx - spread, y), -0.22, unit * (0.52 + vehicleP * 0.2), SignalGold, 0.24 + rms * 0.12);
        DrawHeadlightCone(ctx, new Point(cx + spread, y), 0.22, unit * (0.52 + vehicleP * 0.2), SignalGold, 0.24 + rms * 0.12);
        DrawGlowDisc(ctx, new Point(cx - spread, y), radius, SignalGold, 0.42);
        DrawGlowDisc(ctx, new Point(cx + spread, y), radius, SignalGold, 0.42);

        if (p > 0.62)
        {
            using (ctx.PushOpacity(Math.Clamp((p - 0.62) / 0.25, 0, 1)))
                DrawImageCentered(ctx, _oldLogo, new Point(cx, h * 0.36), unit * 0.25, unit * 0.25);
        }
    }

    private void DrawNewFrameChorus(
        DrawingContext ctx,
        double w,
        double h,
        double p,
        double rms,
        double peak,
        float[] spec,
        float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var center = new Point(cx, cy - unit * 0.03);
        var burst = Math.Clamp((p - 0.76) / 0.22, 0, 1);
        var logoPulse = 1.0 + rms * 0.14 + peak * 0.08 + Math.Sin(_positionSec * 5.2) * 0.012;
        var oldLogoSize = unit * (0.34 + burst * 0.05) * logoPulse;
        var newLogoSize = unit * (0.27 + burst * 0.10 + rms * 0.04);
        var crackPressure = Math.Clamp(0.18 + p * 0.95 + peak * 0.12, 0, 1);

        DrawSpectrumBars(ctx, spec, new Rect(w * 0.08, h * 0.16, w * 0.84, h * 0.18), SignalGold, 0.22 + rms * 0.22);
        DrawGlowDisc(ctx, center, unit * (0.22 + rms * 0.06 + burst * 0.08), SignalGold, 0.12 + rms * 0.14 + burst * 0.16);

        if (burst > 0)
        {
            using (ctx.PushOpacity(Ease(burst)))
                DrawImageCentered(ctx, _newLogo, center, newLogoSize, newLogoSize);
        }

        using (ctx.PushOpacity(1.0 - burst * 0.88))
        {
            DrawImageCentered(ctx, _oldLogo, center, oldLogoSize, oldLogoSize);
            DrawCracks(ctx, center.X, center.Y, unit * (0.15 + p * 0.08), crackPressure, rms);
        }

        DrawOrbitingFragments(ctx, cx, cy - unit * 0.03, unit * (0.31 + rms * 0.04 + burst * 0.12), p, peak, burst);
        if (burst > 0.08)
            DrawLogoBurstShards(ctx, center, unit * 0.23, burst, SignalGold, Cyan);

        DrawWaveformLine(ctx, wave, cx, h * 0.77, w * 0.80, unit * (0.07 + rms * 0.05), Cyan, 0.82);
    }

    private void DrawSkies(DrawingContext ctx, double w, double h, double p, double rms, float[] spec)
    {
        var cx = w / 2;
        var unit = Math.Min(w, h);
        var skyTop = Blend(Color.Parse("#09345A"), Color.Parse("#8D5CFF"), p * 0.45);
        var skyBottom = Blend(Color.Parse("#17341F"), Color.Parse("#FF7A45"), p * 0.35);
        DrawGradient(ctx, w, h, skyTop, skyBottom);
        DrawSpectrumBars(ctx, spec, new Rect(0, h * 0.82, w, h * 0.15), Color.FromArgb(255, 255, 255, 255), 0.13 + rms * 0.14);

        var count = 7 + (int)(p * 8);
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)), 2);
        for (var i = 0; i < count; i++)
        {
            var x = (w * 0.08 + i * w * 0.105 + _positionSec * (18 + i)) % (w * 1.12) - w * 0.06;
            var y = h * (0.72 - p * 0.46) - i * h * 0.025 + Math.Sin(_positionSec * 1.8 + i) * h * 0.018;
            DrawBird(ctx, new Point(x, y), unit * (0.022 + rms * 0.006), pen);
        }

        DrawRouteArc(ctx, new Point(w * 0.18, h * 0.68), new Point(w * 0.82, h * 0.34), p, SignalGold);
        DrawRouteArc(ctx, new Point(w * 0.24, h * 0.76), new Point(w * 0.76, h * 0.48), Math.Clamp(p * 1.25, 0, 1), Cyan);
    }

    private void DrawChapterTurn(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var bookW = unit * 0.52;
        var bookH = unit * 0.42;
        var page = new Rect(cx - bookW / 2, cy - bookH / 2, bookW, bookH);

        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(235, 31, 23, 21)), page, 6);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(190, 170, 120, 80)), 2.5), page, 6, 6);
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(120, 255, 220, 180)), 1.2), new Point(cx, page.Y), new Point(cx, page.Bottom));

        for (var i = 0; i < 9; i++)
        {
            var y = page.Y + page.Height * (0.18 + i * 0.075);
            var length = page.Width * (0.26 + 0.08 * (i % 3));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(70, 255, 235, 210)), 1);
            ctx.DrawLine(pen, new Point(page.X + page.Width * 0.10, y), new Point(page.X + page.Width * 0.10 + length, y));
            ctx.DrawLine(pen, new Point(cx + page.Width * 0.07, y), new Point(cx + page.Width * 0.07 + length * 0.9, y));
        }

        var turn = Math.Clamp((p - 0.25) / 0.55, 0, 1);
        if (turn > 0)
        {
            var fold = new Rect(cx, page.Y, page.Width * 0.5 * turn, page.Height);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(170 * turn), 238, 230, 210)), fold, 5);
        }

        using (ctx.PushOpacity(Math.Clamp((p - 0.58) / 0.30, 0, 1)))
            DrawImageCentered(ctx, _oldLogo, new Point(cx, cy), unit * (0.22 + rms * 0.035), unit * (0.22 + rms * 0.035));
    }

    private void DrawFinalBuild(DrawingContext ctx, double w, double h, double p, double rms, double peak, float[] wave)
    {
        var cx = w / 2;
        var cy = h / 2;
        var unit = Math.Min(w, h);
        var untilDrop = Math.Max(0, _toDropSec);
        var pressure = 1.0 - Math.Clamp(untilDrop / 4.19, 0, 1);
        var logoSize = unit * (0.30 + pressure * 0.08 + rms * 0.07 + peak * 0.05);
        var book = new Rect(cx - unit * 0.33, cy - unit * 0.26, unit * 0.66, unit * 0.52);

        DrawRadialBurst(ctx, cx, cy, unit * (0.18 + pressure * 0.34), 28, Color.FromArgb(255, 255, 255, 255), 0.12 + pressure * 0.30);
        DrawGlowDisc(ctx, new Point(cx, cy), unit * (0.24 + pressure * 0.18), Color.FromArgb(255, 255, 255, 255), 0.10 + pressure * 0.28);
        DrawOpenBook(ctx, book, pressure);
        DrawImageCentered(ctx, _oldLogo, new Point(cx, cy), logoSize, logoSize);
        DrawCracks(ctx, cx, cy, unit * (0.18 + pressure * 0.11), Math.Clamp(0.55 + pressure * 0.55, 0, 1), rms);
        DrawLogoBurstShards(ctx, new Point(cx, cy), unit * 0.23, pressure, SignalGold, Color.FromRgb(255, 255, 255));
        DrawWaveformLine(ctx, wave, cx, h * 0.78, w * 0.86, unit * (0.08 + pressure * 0.08), Color.FromArgb(255, 255, 255, 255), 0.72);

        if (pressure > 0.72)
        {
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(70 * (pressure - 0.72) / 0.28), 255, 255, 255)),
                new Rect(0, 0, w, h));
        }
    }

    private void DrawGlobalAudio(DrawingContext ctx, double w, double h, float[] spec, float[] wave, double rms, double peak)
    {
        DrawSideMeters(ctx, spec, w, h, 0.12 + rms * 0.22);
        if (_positionSec is > 6 and < RevealDropTimeSec - 3)
        {
            DrawWaveformLine(ctx, wave, w / 2, h * 0.91, w * 0.84, Math.Min(w, h) * (0.018 + rms * 0.02),
                Color.FromArgb(255, 255, 255, 255), 0.13 + peak * 0.13);
        }
    }

    private void DrawLyrics(DrawingContext ctx, double w, double h)
    {
        if (_lyrics.Count == 0)
            return;

        var idx = GetLyricIndex(_positionSec);
        if (idx < 0)
            return;

        var line = _lyrics[idx];
        var next = idx + 1 < _lyrics.Count ? _lyrics[idx + 1].Time : line.Time + 4.5f;
        var lineP = Progress(_positionSec, line.Time, next);
        var fadeIn = Math.Clamp(Progress(_positionSec, line.Time - 0.16f, line.Time + 0.22f), 0, 1);
        var fadeOut = Math.Clamp(Progress(_positionSec, next - 0.32f, next), 0, 1);
        var alpha = fadeIn * (1 - fadeOut * 0.75);
        if (alpha <= 0.01)
            return;

        var activeWord = GetActiveWordIndex(line, _positionSec);
        var lyricY = h * 0.815;
        var maxWidth = w * 0.82;
        var size = FitSize(line.Text, maxWidth, Math.Min(w, h) * 0.052, FontWeight.Bold);

        if (line.Words.Count > 0)
            DrawWordHighlightedLine(ctx, line, activeWord, new Point(w / 2, lyricY), size, alpha);
        else
        {
            var text = MakeText(line.Text, size, new SolidColorBrush(Color.FromArgb((byte)(235 * alpha), Ink.R, Ink.G, Ink.B)),
                FontWeight.Bold, "Segoe UI Variable Display");
            ctx.DrawText(text, new Point(w / 2 - text.Width / 2, lyricY - text.Height / 2));
        }

        if (idx + 1 < _lyrics.Count && lineP > 0.62)
        {
            var nextLine = _lyrics[idx + 1].Text;
            var nextSize = FitSize(nextLine, w * 0.64, Math.Min(w, h) * 0.026, FontWeight.SemiBold);
            var previewAlpha = Math.Clamp((lineP - 0.62) / 0.28, 0, 1) * 0.42;
            var preview = MakeText(nextLine, nextSize,
                new SolidColorBrush(Color.FromArgb((byte)(180 * previewAlpha), MutedInk.R, MutedInk.G, MutedInk.B)),
                FontWeight.SemiBold, "Segoe UI");
            ctx.DrawText(preview, new Point(w / 2 - preview.Width / 2, lyricY + Math.Min(w, h) * 0.060));
        }
    }

    private int GetLyricIndex(float time)
    {
        for (var i = _lyrics.Count - 1; i >= 0; i--)
        {
            if (time >= _lyrics[i].Time)
                return i;
        }

        return -1;
    }

    private static int GetActiveWordIndex(LyricLine line, float time)
    {
        var active = -1;
        for (var i = 0; i < line.Words.Count; i++)
        {
            if (time >= line.Words[i].Time)
                active = i;
            else
                break;
        }

        return active;
    }

    private void DrawWordHighlightedLine(DrawingContext ctx, LyricLine line, int activeWord, Point center, double size, double alpha)
    {
        var words = line.Words;
        var gap = Math.Max(8, size * 0.34);
        var widths = new double[words.Count];
        var total = 0.0;
        for (var i = 0; i < words.Count; i++)
        {
            var wordSize = size * (i == activeWord ? 1.08 : 1.0);
            widths[i] = MakeText(words[i].Text, wordSize, Brushes.White, FontWeight.Bold, "Segoe UI Variable Display").Width;
            total += widths[i];
            if (i < words.Count - 1)
                total += gap;
        }

        var x = center.X - total / 2;
        for (var i = 0; i < words.Count; i++)
        {
            var isActive = i == activeWord;
            var isPast = i < activeWord;
            var color = isActive ? SignalGold : isPast ? Ink : MutedInk;
            var wordAlpha = isActive ? alpha : alpha * (isPast ? 0.72 : 0.52);
            var weight = isActive ? FontWeight.Bold : FontWeight.SemiBold;
            var word = MakeText(words[i].Text, size * (isActive ? 1.08 : 1.0),
                new SolidColorBrush(Color.FromArgb((byte)(240 * wordAlpha), color.R, color.G, color.B)),
                weight, "Segoe UI Variable Display");
            var y = center.Y - word.Height / 2 - (isActive ? size * 0.035 : 0);

            if (isActive)
            {
                ctx.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(30 * alpha), Signal.R, Signal.G, Signal.B)),
                    new Rect(x - 8, y + word.Height * 0.18, word.Width + 16, word.Height * 0.72), 4);
            }

            ctx.DrawText(word, new Point(x, y));
            x += widths[i] + gap;
        }
    }

    private void DrawTimeline(DrawingContext ctx, double w, double h)
    {
        var margin = 24.0;
        var y = h - 12;
        var width = w - margin * 2;
        var progress = Math.Clamp(_positionSec / RevealDropTimeSec, 0, 1);
        var basePen = new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1);
        var fillPen = new Pen(new SolidColorBrush(Color.FromArgb(220, Signal.R, Signal.G, Signal.B)), 2);
        ctx.DrawLine(basePen, new Point(margin, y), new Point(w - margin, y));
        ctx.DrawLine(fillPen, new Point(margin, y), new Point(margin + width * progress, y));

        foreach (var chapter in Chapters)
        {
            var x = margin + width * Math.Clamp(chapter.Start / RevealDropTimeSec, 0, 1);
            ctx.DrawLine(basePen, new Point(x, y - 4), new Point(x, y + 4));
        }
    }

    private void DrawContinuationTimeline(DrawingContext ctx, double w, double h)
    {
        var margin = 24.0;
        var y = h - 12;
        var width = w - margin * 2;
        var end = Math.Max(_durationSec, 268.48f);
        var progress = Math.Clamp((_positionSec - RevealDropTimeSec) / (end - RevealDropTimeSec), 0, 1);
        var basePen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)), 1);
        var fillPen = new Pen(new SolidColorBrush(Color.FromArgb(190, Cyan.R, Cyan.G, Cyan.B)), 2);
        ctx.DrawLine(basePen, new Point(margin, y), new Point(w - margin, y));
        ctx.DrawLine(fillPen, new Point(margin, y), new Point(margin + width * progress, y));

        foreach (var chapter in ContinuationChapters)
        {
            var x = margin + width * Math.Clamp((chapter.Start - RevealDropTimeSec) / (end - RevealDropTimeSec), 0, 1);
            ctx.DrawLine(basePen, new Point(x, y - 4), new Point(x, y + 4));
        }
    }

    private void DrawSkipButton(DrawingContext ctx)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        const double btnW = 104;
        const double btnH = 32;
        const double margin = 16;
        var bx = w - btnW - margin;
        var by = h - btnH - margin - 8;
        _skipButtonRect = new Rect(bx, by, btnW, btnH);

        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(170, 8, 9, 12)), _skipButtonRect, 4);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(115, 255, 255, 255)), 1), _skipButtonRect, 4, 4);
        DrawFastForwardGlyph(ctx, new Point(bx + 19, by + btnH / 2), 10, MutedInk);

        var lbl = MakeText("Skip", 12, new SolidColorBrush(MutedInk), FontWeight.SemiBold, "Segoe UI");
        ctx.DrawText(lbl, new Point(bx + 38, by + (btnH - lbl.Height) / 2));
        var esc = MakeText("Esc", 10, new SolidColorBrush(DimInk), FontWeight.Normal, "Cascadia Mono");
        ctx.DrawText(esc, new Point(bx + btnW - esc.Width - 12, by + (btnH - esc.Height) / 2));
    }

    private void RenderGlitch(DrawingContext ctx)
    {
        if (_snapshot is null)
        {
            DrawReveal(ctx);
            return;
        }

        var gf = _animFrame - FlashEnd;
        var progress = gf / (double)(GlitchEnd - FlashEnd);
        var srcRect = new Rect(0, 0, _snapshot.PixelSize.Width, _snapshot.PixelSize.Height);
        var dstRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var bandH = Bounds.Height / _bandOffsets.Length;

        for (var i = 0; i < _bandOffsets.Length; i++)
        {
            var dy = Math.Sin(i * 2.3 + gf * 3.4) * 12 * progress;
            var dx = Math.Cos(i * 1.7 + gf * 2.1) * 7 * progress;
            using (ctx.PushClip(new Rect(0, i * bandH, Bounds.Width, bandH + 1)))
            using (ctx.PushTransform(Matrix.CreateTranslation(dx, dy)))
                ctx.DrawImage(_snapshot, srcRect, dstRect);
        }

        using (ctx.PushOpacity(0.20 + progress * 0.15))
        {
            using (ctx.PushTransform(Matrix.CreateTranslation(-12 * progress, 0)))
                ctx.DrawImage(_snapshot, srcRect, dstRect);
            using (ctx.PushTransform(Matrix.CreateTranslation(16 * progress, 0)))
                ctx.DrawImage(_snapshot, srcRect, dstRect);
        }

        var scan = new SolidColorBrush(Color.FromArgb((byte)(70 + 90 * progress), 255, 255, 255));
        for (var sy = 0.0; sy < Bounds.Height; sy += 5)
            ctx.FillRectangle(scan, new Rect(0, sy, Bounds.Width, 1));
    }

    private void RenderShatter(DrawingContext ctx)
    {
        if (_snapshot is null)
            return;

        var sf = _animFrame - GlitchEnd;
        var srcRect = new Rect(0, 0, _snapshot.PixelSize.Width, _snapshot.PixelSize.Height);
        var dstRect = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var bandH = Bounds.Height / _bandOffsets.Length;

        using (ctx.PushOpacity(_shatterAlpha))
        {
            for (var i = 0; i < _bandOffsets.Length; i++)
            {
                using (ctx.PushClip(new Rect(0, i * bandH, Bounds.Width, bandH + 1)))
                using (ctx.PushTransform(Matrix.CreateTranslation(_bandOffsets[i], (i % 3 - 1) * sf * 1.5)))
                    ctx.DrawImage(_snapshot, srcRect, dstRect);
            }
        }

        var maxR = Math.Sqrt(Bounds.Width * Bounds.Width + Bounds.Height * Bounds.Height) * 0.62;
        var r = maxR * Math.Clamp(sf / 9.0, 0, 1);
        var ringA = (byte)(230 * Math.Pow(Math.Clamp(1.0 - sf / 9.0, 0, 1), 1.35));
        if (ringA > 0 && r > 1)
            ctx.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(ringA, 255, 255, 255)), 5 + sf), Bounds.Center, r, r);

        var flashA = (byte)(160 * Math.Pow(Math.Clamp(1.0 - sf / 5.0, 0, 1), 2));
        if (flashA > 0)
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(flashA, 255, 255, 255)), new Rect(Bounds.Size));
    }

    private static Bitmap? LoadBitmap(string name)
    {
        foreach (var path in CandidateAssetPaths(name))
        {
            if (File.Exists(path))
                return new Bitmap(path);
        }

        return null;
    }

    private static List<LyricLine> LoadLyrics()
    {
        foreach (var path in CandidateAssetPaths("reveal.lrc"))
        {
            if (!File.Exists(path))
                continue;

            return File.ReadLines(path)
                .Select(ParseLyricLine)
                .Where(line => line is not null)
                .Select(line => line!)
                .OrderBy(line => line.Time)
                .ToList();
        }

        return [];
    }

    private static IEnumerable<string> CandidateAssetPaths(string fileName)
    {
        yield return Path.Combine(AppContext.BaseDirectory, "assets", "audio", fileName);
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "audio", fileName);
        yield return Path.Combine(Environment.CurrentDirectory, "assets", "audio", fileName);
        yield return Path.Combine(Environment.CurrentDirectory, "Assets", "audio", fileName);
        yield return Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Assets", "audio", fileName);
    }

    private static LyricLine? ParseLyricLine(string raw)
    {
        var match = LrcLineRegex.Match(raw.Trim());
        if (!match.Success)
            return null;

        var lineTime = ToSeconds(match.Groups[1].Value, match.Groups[2].Value);
        var taggedText = match.Groups[3].Value.Trim();
        if (taggedText.Length == 0)
            return null;

        var words = new List<LyricWord>();
        var cursor = 0;
        foreach (Match wordMatch in WordTimeRegex.Matches(taggedText))
        {
            var segment = taggedText[cursor..wordMatch.Index].Trim();
            var wordTime = ToSeconds(wordMatch.Groups[1].Value, wordMatch.Groups[2].Value);
            AddLyricWords(words, segment, wordTime);
            cursor = wordMatch.Index + wordMatch.Length;
        }

        if (cursor < taggedText.Length)
            AddLyricWords(words, taggedText[cursor..].Trim(), words.Count > 0 ? words[^1].Time + 0.18f : lineTime);

        var clean = WordTimeRegex.Replace(taggedText, " ");
        clean = Regex.Replace(clean, @"\s+", " ").Trim();
        return new LyricLine(lineTime, clean, words);
    }

    private static void AddLyricWords(List<LyricWord> words, string segment, float time)
    {
        if (string.IsNullOrWhiteSpace(segment))
            return;

        foreach (var word in segment.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            words.Add(new LyricWord(time, word));
    }

    private static float ToSeconds(string minutes, string seconds) =>
        int.Parse(minutes, CultureInfo.InvariantCulture) * 60 + float.Parse(seconds, CultureInfo.InvariantCulture);

    private static void DrawGradient(DrawingContext ctx, double w, double h, Color top, Color bottom)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
            GradientStops = new GradientStops { new(top, 0), new(bottom, 1) }
        };
        ctx.FillRectangle(brush, new Rect(0, 0, w, h));
    }

    private static void DrawVignette(DrawingContext ctx, double w, double h)
    {
        var bands = 8;
        for (var i = 0; i < bands; i++)
        {
            var inset = i * Math.Min(w, h) * 0.025;
            var alpha = (byte)(18 + i * 12);
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0)), Math.Min(w, h) * 0.040),
                new Rect(inset, inset, w - inset * 2, h - inset * 2), 0, 0);
        }
    }

    private static void DrawPerspectiveGrid(DrawingContext ctx, double w, double h, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        var horizon = h * 0.64;
        for (var i = -8; i <= 8; i++)
        {
            var x = w / 2 + i * w * 0.085;
            ctx.DrawLine(pen, new Point(x, h), new Point(w / 2 + i * w * 0.012, horizon));
        }

        for (var i = 0; i < 9; i++)
        {
            var t = i / 8.0;
            var y = horizon + (h - horizon) * t * t;
            ctx.DrawLine(pen, new Point(0, y), new Point(w, y));
        }
    }

    private static void DrawFilmLines(DrawingContext ctx, double w, double h, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, 255, 255, 255));
        for (var y = 0.0; y < h; y += 7)
            ctx.FillRectangle(brush, new Rect(0, y, w, 0.7));
    }

    private static void DrawBlueprintGrid(DrawingContext ctx, double w, double h, double opacity)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 * opacity), 78, 194, 255)), 0.8);
        for (var x = 0.0; x <= w; x += 38)
            ctx.DrawLine(pen, new Point(x, 0), new Point(x, h));
        for (var y = 0.0; y <= h; y += 38)
            ctx.DrawLine(pen, new Point(0, y), new Point(w, y));
    }

    private static void DrawStars(DrawingContext ctx, double w, double h, int count, int seed, double opacity)
    {
        var rng = new Random(seed);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(160 * opacity), 255, 255, 255));
        for (var i = 0; i < count; i++)
        {
            var size = 0.8 + rng.NextDouble() * 1.8;
            ctx.FillRectangle(brush, new Rect(rng.NextDouble() * w, rng.NextDouble() * h * 0.66, size, size));
        }
    }

    private static void DrawImageCentered(DrawingContext ctx, Bitmap? image, Point center, double width, double height)
    {
        if (image is null)
            return;

        var src = new Rect(0, 0, image.PixelSize.Width, image.PixelSize.Height);
        var dst = new Rect(center.X - width / 2, center.Y - height / 2, width, height);
        ctx.DrawImage(image, src, dst);
    }

    private static void DrawGlowDisc(DrawingContext ctx, Point center, double radius, Color color, double alpha)
    {
        for (var i = 5; i >= 1; i--)
        {
            var a = (byte)(255 * alpha * (1.0 - i / 6.0) * 0.22);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B)), null,
                center, radius + i * radius * 0.22, radius + i * radius * 0.22);
        }
    }

    private static void DrawRadialBurst(DrawingContext ctx, double cx, double cy, double radius, int count, Color color, double alpha)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B)), 1.4);
        for (var i = 0; i < count; i++)
        {
            var a = i * Math.PI * 2 / count;
            var start = new Point(cx + Math.Cos(a) * radius * 0.25, cy + Math.Sin(a) * radius * 0.25);
            var end = new Point(cx + Math.Cos(a) * radius, cy + Math.Sin(a) * radius);
            ctx.DrawLine(pen, start, end);
        }
    }

    private static void DrawBlueprintSheet(DrawingContext ctx, Rect panel, double p)
    {
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(210, 8, 24, 40)), panel, 6);
        ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(180, 86, 196, 255)), 1.6), panel, 6, 6);
        var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(46, 107, 211, 255)), 0.7);
        for (var x = panel.X; x < panel.Right; x += 24)
            ctx.DrawLine(gridPen, new Point(x, panel.Y), new Point(x, panel.Bottom));
        for (var y = panel.Y; y < panel.Bottom; y += 24)
            ctx.DrawLine(gridPen, new Point(panel.X, y), new Point(panel.Right, y));

        var sweep = panel.X + panel.Width * p;
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)), new Rect(sweep - 18, panel.Y, 36, panel.Height));
    }

    private static void DrawErrorNeedles(DrawingContext ctx, double cx, double cy, double unit, double p, double rms)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(170 + 60 * rms), 255, 78, 64)), 2.2);
        for (var i = 0; i < 10; i++)
        {
            var a = i * Math.PI * 2 / 10 + p * 0.7;
            var r1 = unit * (0.10 + 0.02 * (i % 2));
            var r2 = unit * (0.18 + p * 0.08);
            ctx.DrawLine(pen, new Point(cx + Math.Cos(a) * r1, cy + Math.Sin(a) * r1),
                new Point(cx + Math.Cos(a) * r2, cy + Math.Sin(a) * r2));
        }
    }

    private static void DrawSunsetBands(DrawingContext ctx, double w, double h, double p)
    {
        var bands = new[]
        {
            Color.Parse("#34101C"),
            Color.Parse("#74321C"),
            Color.Parse("#B9572E"),
            Color.Parse("#F2A34D"),
            Color.Parse("#4D2547")
        };

        for (var i = 0; i < bands.Length; i++)
        {
            var y = h * (0.20 + i * 0.10 - p * 0.025);
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(72, bands[i].R, bands[i].G, bands[i].B)),
                new Rect(0, y, w, h * 0.085));
        }
    }

    private static void DrawWindowFrame(DrawingContext ctx, Rect rect, Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        ctx.DrawRectangle(null, pen, rect, 3, 3);
        ctx.DrawLine(pen, new Point(rect.Center.X, rect.Y), new Point(rect.Center.X, rect.Bottom));
        ctx.DrawLine(pen, new Point(rect.X, rect.Center.Y), new Point(rect.Right, rect.Center.Y));
    }

    private static void DrawPlayGlyph(DrawingContext ctx, Point center, double size, Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        ctx.DrawLine(pen, new Point(center.X - size * 0.38, center.Y - size * 0.50), new Point(center.X + size * 0.48, center.Y));
        ctx.DrawLine(pen, new Point(center.X + size * 0.48, center.Y), new Point(center.X - size * 0.38, center.Y + size * 0.50));
        ctx.DrawLine(pen, new Point(center.X - size * 0.38, center.Y + size * 0.50), new Point(center.X - size * 0.38, center.Y - size * 0.50));
    }

    private static void DrawIsoFrame(DrawingContext ctx, Point center, double size, Color color, double opacity)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B)), 2);
        var pts = new Point[6];
        for (var i = 0; i < 6; i++)
        {
            var angle = (30 + i * 60) * Math.PI / 180;
            pts[i] = new Point(center.X + Math.Cos(angle) * size, center.Y + Math.Sin(angle) * size * 0.72);
        }

        for (var i = 0; i < 6; i++)
            ctx.DrawLine(pen, pts[i], pts[(i + 1) % 6]);
        ctx.DrawLine(pen, center, pts[0]);
        ctx.DrawLine(pen, center, pts[2]);
        ctx.DrawLine(pen, center, pts[4]);
    }

    private static void DrawHouseMark(DrawingContext ctx, Point center, double size, Color color, double opacity)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B)), 2.2);
        var roof = new Point(center.X, center.Y - size * 0.45);
        var left = new Point(center.X - size * 0.46, center.Y);
        var right = new Point(center.X + size * 0.46, center.Y);
        var floorLeft = new Point(center.X - size * 0.38, center.Y + size * 0.42);
        var floorRight = new Point(center.X + size * 0.38, center.Y + size * 0.42);
        ctx.DrawLine(pen, left, roof);
        ctx.DrawLine(pen, roof, right);
        ctx.DrawLine(pen, left, floorLeft);
        ctx.DrawLine(pen, right, floorRight);
        ctx.DrawLine(pen, floorLeft, floorRight);
    }

    private static void DrawCodeRails(DrawingContext ctx, double w, double h, double p, double rms)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(70 + rms * 80), 210, 255, 230)), 1);
        for (var i = 0; i < 10; i++)
        {
            var y = h * (0.18 + i * 0.055);
            var x1 = w * 0.12;
            var x2 = x1 + w * (0.10 + 0.05 * (i % 4)) * Math.Clamp(p * 1.8 - i * 0.08, 0, 1);
            ctx.DrawLine(pen, new Point(x1, y), new Point(x2, y));
        }
    }

    private static void DrawPerson(DrawingContext ctx, Point feet, double size, Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(color), thickness);
        ctx.DrawEllipse(null, pen, new Point(feet.X, feet.Y - size * 0.78), size * 0.16, size * 0.16);
        ctx.DrawLine(pen, new Point(feet.X, feet.Y - size * 0.62), new Point(feet.X, feet.Y - size * 0.18));
        ctx.DrawLine(pen, new Point(feet.X, feet.Y - size * 0.46), new Point(feet.X - size * 0.28, feet.Y - size * 0.30));
        ctx.DrawLine(pen, new Point(feet.X, feet.Y - size * 0.46), new Point(feet.X + size * 0.30, feet.Y - size * 0.32));
        ctx.DrawLine(pen, new Point(feet.X, feet.Y - size * 0.18), new Point(feet.X - size * 0.18, feet.Y));
        ctx.DrawLine(pen, new Point(feet.X, feet.Y - size * 0.18), new Point(feet.X + size * 0.22, feet.Y));
    }

    private static void DrawBrickLines(DrawingContext ctx, Rect wall, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1);
        for (var y = wall.Y + 24; y < wall.Bottom; y += 24)
            ctx.DrawLine(pen, new Point(wall.X, y), new Point(wall.Right, y));
        for (var y = wall.Y; y < wall.Bottom; y += 48)
        {
            for (var x = wall.X + 30; x < wall.Right; x += 60)
                ctx.DrawLine(pen, new Point(x, y), new Point(x, Math.Min(wall.Bottom, y + 24)));
        }
    }

    private static void DrawRoad(DrawingContext ctx, double cx, double horizon, double w, double h, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.4);
        for (var i = -7; i <= 7; i++)
            ctx.DrawLine(pen, new Point(cx + i * w * 0.12, h), new Point(cx + i * w * 0.016, horizon));
        ctx.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, color.R, color.G, color.B)), 2),
            new Point(0, horizon), new Point(w, horizon));
    }

    private static void DrawChain(DrawingContext ctx, double cx, double cy, double width, double breakP, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 2.7);
        var r = width * 0.055;
        for (var i = 0; i < 5; i++)
        {
            var leftShift = breakP > 0.42 ? -(breakP - 0.42) * width * 0.22 : 0;
            var rightShift = breakP > 0.42 ? (breakP - 0.42) * width * 0.22 : 0;
            ctx.DrawEllipse(null, pen, new Point(cx - width * 0.11 * (5 - i) + leftShift, cy), r, r * 0.55);
            ctx.DrawEllipse(null, pen, new Point(cx + width * 0.11 * (i + 1) + rightShift, cy), r, r * 0.55);
        }
    }

    private static void DrawSpectrumRing(DrawingContext ctx, Point center, double radius, float[] spec, double opacity)
    {
        if (spec.Length == 0)
            return;

        var count = Math.Min(spec.Length, 72);
        for (var i = 0; i < count; i++)
        {
            var value = Math.Clamp(spec[i], 0, 1);
            var a = i * Math.PI * 2 / count - Math.PI / 2;
            var inner = radius;
            var outer = radius + value * radius * 0.36;
            var color = Blend(Cyan, SignalGold, i / (double)count);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(210 * opacity), color.R, color.G, color.B)), 2);
            ctx.DrawLine(pen, new Point(center.X + Math.Cos(a) * inner, center.Y + Math.Sin(a) * inner),
                new Point(center.X + Math.Cos(a) * outer, center.Y + Math.Sin(a) * outer));
        }
    }

    private static void DrawMonitor(DrawingContext ctx, Rect rect, Color color, double thickness)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(220, color.R, color.G, color.B)), thickness);
        ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(130, 0, 0, 0)), rect, 4);
        ctx.DrawRectangle(null, pen, rect, 4, 4);
        ctx.DrawLine(pen, new Point(rect.Center.X, rect.Bottom), new Point(rect.Center.X, rect.Bottom + rect.Height * 0.18));
        ctx.DrawLine(pen, new Point(rect.Center.X - rect.Width * 0.16, rect.Bottom + rect.Height * 0.18),
            new Point(rect.Center.X + rect.Width * 0.16, rect.Bottom + rect.Height * 0.18));
    }

    private static void DrawDoor(DrawingContext ctx, Rect rect, double p)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(230 * p), 255, 245, 180)), 2.4);
        ctx.DrawLine(pen, new Point(rect.X, rect.Bottom), new Point(rect.X, rect.Y));
        ctx.DrawLine(pen, new Point(rect.X, rect.Y), new Point(rect.Right, rect.Y));
        ctx.DrawLine(pen, new Point(rect.Right, rect.Y), new Point(rect.Right, rect.Bottom));
        ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(220 * p), 255, 245, 180)), null,
            new Point(rect.Right - rect.Width * 0.22, rect.Center.Y), 2.2, 2.2);
    }

    private static void DrawHeadlightCone(DrawingContext ctx, Point origin, double skew, double length, Color color, double alpha)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B)), 1.4);
        for (var i = -3; i <= 3; i++)
        {
            var spread = (i / 3.0) * length * 0.32;
            ctx.DrawLine(pen, origin, new Point(origin.X + spread + skew * length, origin.Y + length));
        }
    }

    private static void DrawOrbitingFragments(DrawingContext ctx, double cx, double cy, double radius, double p, double peak, double burst = 0)
    {
        for (var i = 0; i < 10; i++)
        {
            var angle = i * Math.PI * 2 / 10 + p * Math.PI * 2;
            var flyout = radius * burst * burst * (0.45 + (i % 4) * 0.12);
            var pos = new Point(
                cx + Math.Cos(angle) * (radius + flyout),
                cy + Math.Sin(angle) * (radius * 0.74 + flyout * 0.72));
            var color = Blend(Cyan, Signal, i / 9.0);
            var alpha = (byte)Math.Clamp(150 + 70 * peak - burst * 80, 40, 235);
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), 1.6 + burst * 1.4);
            var s = radius * (0.045 + 0.015 * (i % 3)) * (1 + burst * 0.65);
            ctx.DrawRectangle(null, pen, new Rect(pos.X - s / 2, pos.Y - s / 2, s, s), 1, 1);
        }
    }

    private static void DrawLogoBurstShards(DrawingContext ctx, Point center, double radius, double burst, Color a, Color b)
    {
        burst = Math.Clamp(burst, 0, 1);
        if (burst <= 0)
            return;

        for (var i = 0; i < 18; i++)
        {
            var angle = i * Math.PI * 2 / 18 + Math.Sin(i * 1.91) * 0.18;
            var distance = radius * (0.62 + burst * (0.8 + (i % 5) * 0.22));
            var pos = new Point(center.X + Math.Cos(angle) * distance, center.Y + Math.Sin(angle) * distance);
            var color = Blend(a, b, i / 17.0);
            var alpha = (byte)(210 * Math.Pow(1 - burst * 0.45, 1.2));
            var pen = new Pen(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)), 1.2 + burst * 1.8);
            var size = radius * (0.055 + 0.018 * (i % 4)) * (1 + burst * 0.85);

            ctx.DrawLine(pen, new Point(pos.X - size, pos.Y - size * 0.35), new Point(pos.X + size, pos.Y + size * 0.35));
            ctx.DrawLine(pen, new Point(pos.X + size, pos.Y + size * 0.35), new Point(pos.X - size * 0.2, pos.Y + size));
            ctx.DrawLine(pen, new Point(pos.X - size * 0.2, pos.Y + size), new Point(pos.X - size, pos.Y - size * 0.35));
        }
    }

    private static void DrawOpenBook(DrawingContext ctx, Rect rect, double pressure)
    {
        var cover = new SolidColorBrush(Color.FromArgb(190, 42, 28, 24));
        var page = new SolidColorBrush(Color.FromArgb(90, 238, 226, 206));
        var edgePen = new Pen(new SolidColorBrush(Color.FromArgb(170, 185, 126, 72)), 2.2);
        var pagePen = new Pen(new SolidColorBrush(Color.FromArgb(65, 255, 232, 190)), 1.0);
        var open = Math.Clamp(0.55 + pressure * 0.35, 0, 1);

        var left = new Rect(rect.X - rect.Width * 0.10 * open, rect.Y, rect.Width * 0.50, rect.Height);
        var right = new Rect(rect.Center.X + rect.Width * 0.10 * open, rect.Y, rect.Width * 0.50, rect.Height);
        ctx.FillRectangle(cover, left, 5);
        ctx.FillRectangle(cover, right, 5);
        ctx.FillRectangle(page, new Rect(left.X + left.Width * 0.08, left.Y + left.Height * 0.08, left.Width * 0.84, left.Height * 0.84), 4);
        ctx.FillRectangle(page, new Rect(right.X + right.Width * 0.08, right.Y + right.Height * 0.08, right.Width * 0.84, right.Height * 0.84), 4);
        ctx.DrawRectangle(null, edgePen, left, 5, 5);
        ctx.DrawRectangle(null, edgePen, right, 5, 5);

        for (var i = 0; i < 8; i++)
        {
            var y = rect.Y + rect.Height * (0.18 + i * 0.08);
            ctx.DrawLine(pagePen, new Point(left.X + left.Width * 0.15, y), new Point(left.Right - left.Width * 0.16, y));
            ctx.DrawLine(pagePen, new Point(right.X + right.Width * 0.15, y), new Point(right.Right - right.Width * 0.16, y));
        }
    }

    private static void DrawWindowReplayTiles(DrawingContext ctx, double cx, double cy, double cube, double p)
    {
        var colors = new[]
        {
            Color.FromArgb(180, 103, 214, 255),
            Color.FromArgb(180, 255, 184, 77),
            Color.FromArgb(180, 77, 255, 176),
            Color.FromArgb(180, 255, 78, 26)
        };

        for (var i = 0; i < colors.Length; i++)
        {
            var angle = -Math.PI * 0.75 + i * Math.PI * 0.5 + p * 0.45;
            var rect = new Rect(
                cx + Math.Cos(angle) * cube * 0.92 - cube * 0.14,
                cy + Math.Sin(angle) * cube * 0.55 - cube * 0.08,
                cube * 0.28,
                cube * 0.16);
            DrawMonitor(ctx, rect, colors[i], 1.4);
            ctx.DrawLine(new Pen(new SolidColorBrush(colors[i]), 1.1),
                new Point(rect.X + rect.Width * 0.16, rect.Center.Y),
                new Point(rect.Right - rect.Width * 0.16, rect.Center.Y + Math.Sin(i + p * 6) * rect.Height * 0.18));
        }
    }

    private static void DrawNetworkBranches(DrawingContext ctx, double w, double h, double p)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(130, 255, 184, 77)), 1.4);
        var root = new Point(w / 2, h * 0.70);
        for (var i = 0; i < 18; i++)
        {
            var t = Math.Clamp(p * 1.4 - i * 0.035, 0, 1);
            if (t <= 0)
                continue;

            var angle = -Math.PI + i * Math.PI / 17;
            var len = w * (0.16 + 0.22 * ((i % 4) / 3.0)) * t;
            var end = new Point(root.X + Math.Cos(angle) * len, root.Y + Math.Sin(angle) * len * 0.55);
            ctx.DrawLine(pen, root, end);
            ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(110, 255, 184, 77)), null, end, 3 + t * 3, 3 + t * 3);
        }
    }

    private static void DrawDeviceSunriseStrip(DrawingContext ctx, double w, double h, double p, float[] wave)
    {
        var labels = new[] { "WIN", "MAC", "LINUX", "MOBILE" };
        var colors = new[] { Color.Parse("#3AA8FF"), Color.Parse("#E8EAEE"), Color.Parse("#FFB04A"), Color.Parse("#4DFFB0") };
        var width = w * 0.13;
        var height = h * 0.10;
        var startX = w * 0.18;
        var y = h * 0.20;
        for (var i = 0; i < labels.Length; i++)
        {
            var reveal = Math.Clamp(p * 3.6 - i * 0.45, 0, 1);
            if (reveal <= 0)
                continue;

            var rect = new Rect(startX + i * w * 0.17, y + Math.Sin(i + p * 4) * h * 0.012, width, height);
            using (ctx.PushOpacity(reveal))
            {
                DrawMonitor(ctx, rect, colors[i], 1.6);
                var sun = new Point(rect.Center.X, rect.Bottom - rect.Height * 0.24);
                ctx.DrawEllipse(new SolidColorBrush(Color.FromArgb(160, 255, 184, 77)), null, sun, rect.Height * 0.18, rect.Height * 0.18);
                DrawWaveformLine(ctx, wave, rect.Center.X, rect.Center.Y, rect.Width * 0.68, rect.Height * 0.14, colors[i], 0.55);
                var label = MakeText(labels[i], Math.Max(9, h * 0.014), new SolidColorBrush(colors[i]), FontWeight.Bold, "Cascadia Mono");
                ctx.DrawText(label, new Point(rect.Center.X - label.Width / 2, rect.Bottom + 4));
            }
        }
    }

    private static void DrawOrbitingUiPanels(DrawingContext ctx, Point center, double radius, double p, double peak)
    {
        var names = new[] { "LIB", "PLAY", "SET", "VIZ", "OBS", "SYNC" };
        for (var i = 0; i < names.Length; i++)
        {
            var angle = i * Math.PI * 2 / names.Length + p * Math.PI * 2;
            var pos = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius * 0.62);
            var rect = new Rect(pos.X - radius * 0.13, pos.Y - radius * 0.055, radius * 0.26, radius * 0.11);
            var color = Blend(Cyan, SignalGold, i / (double)(names.Length - 1));
            ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(78, 0, 0, 0)), rect, 3);
            ctx.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(120 + peak * 90), color.R, color.G, color.B)), 1.2), rect, 3, 3);
            var text = MakeText(names[i], Math.Max(8, radius * 0.035), new SolidColorBrush(color), FontWeight.Bold, "Cascadia Mono");
            ctx.DrawText(text, new Point(rect.Center.X - text.Width / 2, rect.Center.Y - text.Height / 2));
        }
    }

    private static void DrawWireframeFoundation(DrawingContext ctx, Rect rect, double p, Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, color.R, color.G, color.B)), 1.2);
        ctx.DrawRectangle(null, pen, rect, 2, 2);
        for (var i = 1; i < 9; i++)
        {
            var x = rect.X + rect.Width * i / 9;
            ctx.DrawLine(pen, new Point(x, rect.Y), new Point(x - rect.Width * 0.08 * p, rect.Bottom));
        }
        for (var i = 1; i < 4; i++)
        {
            var y = rect.Y + rect.Height * i / 4;
            ctx.DrawLine(pen, new Point(rect.X, y), new Point(rect.Right, y));
        }
    }

    private static void DrawClosingBlueprintBook(DrawingContext ctx, Rect rect, double closeP)
    {
        if (closeP <= 0)
            return;

        var alpha = (byte)(145 * closeP);
        var cover = new SolidColorBrush(Color.FromArgb(alpha, 8, 24, 40));
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(170 * closeP), 78, 194, 255)), 1.5);
        var left = new Rect(rect.X, rect.Y, rect.Width * (0.5 + closeP * 0.18), rect.Height);
        var right = new Rect(rect.Right - rect.Width * (0.5 + closeP * 0.18), rect.Y, rect.Width * (0.5 + closeP * 0.18), rect.Height);
        ctx.FillRectangle(cover, left, 5);
        ctx.FillRectangle(cover, right, 5);
        ctx.DrawRectangle(null, pen, left, 5, 5);
        ctx.DrawRectangle(null, pen, right, 5, 5);
    }

    private static void DrawBird(DrawingContext ctx, Point center, double size, Pen pen)
    {
        ctx.DrawLine(pen, new Point(center.X - size, center.Y + size * 0.35), center);
        ctx.DrawLine(pen, center, new Point(center.X + size, center.Y + size * 0.35));
    }

    private static void DrawRouteArc(DrawingContext ctx, Point start, Point end, double p, Color color)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)), 2);
        var steps = Math.Max(2, (int)(30 * p));
        Point? previous = null;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / 30.0;
            if (t > p)
                break;

            var x = start.X + (end.X - start.X) * t;
            var y = start.Y + (end.Y - start.Y) * t - Math.Sin(t * Math.PI) * Math.Abs(end.X - start.X) * 0.18;
            var point = new Point(x, y);
            if (previous is { } prev)
                ctx.DrawLine(pen, prev, point);
            previous = point;
        }
    }

    private static void DrawCracks(DrawingContext ctx, double cx, double cy, double radius, double pressure, double rms)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(130 + pressure * 110), 255, 250, 240)),
            1.5 + pressure * 2.8 + rms);
        var count = 6 + (int)(pressure * 10);
        for (var i = 0; i < count; i++)
        {
            var angle = i * Math.PI * 2 / count + 0.25 * Math.Sin(i);
            var fork = angle + 0.28 * Math.Sin(i * 2.7);
            var start = new Point(cx + Math.Cos(angle) * radius * 0.42, cy + Math.Sin(angle) * radius * 0.42);
            var mid = new Point(cx + Math.Cos(angle) * radius * (0.8 + pressure * 0.5), cy + Math.Sin(angle) * radius * (0.8 + pressure * 0.5));
            var end = new Point(cx + Math.Cos(fork) * radius * (1.25 + pressure * 1.4), cy + Math.Sin(fork) * radius * (1.25 + pressure * 1.4));
            ctx.DrawLine(pen, start, mid);
            ctx.DrawLine(pen, mid, end);
        }
    }

    private static void DrawWaveformLine(DrawingContext ctx, float[] wave, double cx, double cy, double width, double amplitude, Color color, double alpha)
    {
        if (wave.Length < 2)
            return;

        var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), color.R, color.G, color.B)), 1.7);
        var n = Math.Min(wave.Length, 256);
        for (var i = 0; i < n - 1; i++)
        {
            var x1 = cx - width / 2 + i * width / (n - 1);
            var x2 = cx - width / 2 + (i + 1) * width / (n - 1);
            ctx.DrawLine(pen, new Point(x1, cy + wave[i] * amplitude), new Point(x2, cy + wave[i + 1] * amplitude));
        }
    }

    private static void DrawSpectrumBars(DrawingContext ctx, float[] spec, Rect area, Color color, double alpha)
    {
        if (spec.Length == 0)
            return;

        var n = Math.Min(spec.Length, 96);
        var bw = area.Width / n * 0.70;
        for (var i = 0; i < n; i++)
        {
            var value = Math.Clamp(spec[i], 0, 1);
            var barColor = Blend(color, Cyan, i / (double)n * 0.45);
            var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), barColor.R, barColor.G, barColor.B));
            var bh = value * area.Height;
            ctx.FillRectangle(brush, new Rect(area.X + i * area.Width / n, area.Bottom - bh, bw, bh), 1);
        }
    }

    private static void DrawSideMeters(DrawingContext ctx, float[] spec, double w, double h, double alpha)
    {
        if (spec.Length == 0)
            return;

        var n = Math.Min(spec.Length, 40);
        var brush = new SolidColorBrush(Color.FromArgb((byte)(255 * alpha), Signal.R, Signal.G, Signal.B));
        for (var i = 0; i < n; i++)
        {
            var bh = Math.Clamp(spec[i], 0, 1) * h * 0.16;
            var y = h * 0.18 + i * h * 0.015;
            ctx.FillRectangle(brush, new Rect(0, y, 3 + bh * 0.22, 2));
            ctx.FillRectangle(brush, new Rect(w - 3 - bh * 0.22, y, 3 + bh * 0.22, 2));
        }
    }

    private static void DrawFastForwardGlyph(DrawingContext ctx, Point center, double size, Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.6);
        var x = center.X - size * 0.62;
        for (var i = 0; i < 2; i++)
        {
            var offset = i * size * 0.55;
            ctx.DrawLine(pen, new Point(x + offset, center.Y - size * 0.48), new Point(x + offset + size * 0.42, center.Y));
            ctx.DrawLine(pen, new Point(x + offset + size * 0.42, center.Y), new Point(x + offset, center.Y + size * 0.48));
            ctx.DrawLine(pen, new Point(x + offset, center.Y + size * 0.48), new Point(x + offset, center.Y - size * 0.48));
        }
    }

    private static FormattedText MakeText(
        string text,
        double size,
        IBrush brush,
        FontWeight weight = FontWeight.Normal,
        string fontFamily = "Segoe UI") =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(fontFamily, FontStyle.Normal, weight),
            size,
            brush);

    private static double FitSize(string text, double maxWidth, double preferred, FontWeight weight)
    {
        var size = preferred;
        while (size > 14)
        {
            var ft = MakeText(text, size, Brushes.White, weight, "Segoe UI Variable Display");
            if (ft.Width <= maxWidth)
                return size;
            size -= 1.5;
        }

        return size;
    }

    private static double Progress(float t, float start, float end) =>
        end <= start ? 1 : Math.Clamp((t - start) / (end - start), 0, 1);

    private static double Ease(double t) =>
        t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t);

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static Color Blend(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private sealed record Chapter(float Start, string Name, string Top, string Bottom, Mood Mood);
    private sealed record LyricLine(float Time, string Text, List<LyricWord> Words);
    private sealed record LyricWord(float Time, string Text);

    private enum Mood
    {
        Nocturne,
        Blueprint,
        Horizon,
        Workbench,
        Breakout,
        Pulse,
        Platforms,
        Headlights,
        Chorus,
        Skies,
        Chapter,
        Final,
        Afterglow,
        BridgeHouse,
        FinalNetwork,
        PrototypeOutro
    }
}
