using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Effects;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.Views;

public partial class KaraokeWindow : Window
{
    private readonly AudioEngine _engine;
    private readonly EffectChain _effectChain;
    private readonly Action _togglePlayback;
    private readonly Func<LyricsDocument?> _getLyrics;
    private readonly DispatcherTimer _timer;
    private VocalBlendEffect? _vocalEffect;
    private LyricsDocument? _lastDocument;

    public KaraokeWindow(
        AudioEngine engine,
        EffectChain effectChain,
        Action togglePlayback,
        Func<LyricsDocument?> getLyrics)
    {
        _engine = engine;
        _effectChain = effectChain;
        _togglePlayback = togglePlayback;
        _getLyrics = getLyrics;

        InitializeComponent();

        // Find or add a VocalBlendEffect starting at blend=0 (audibly transparent).
        _vocalEffect = _effectChain.Effects.OfType<VocalBlendEffect>().FirstOrDefault();
        if (_vocalEffect is null)
        {
            _vocalEffect = new VocalBlendEffect();
            _vocalEffect.Parameters.Set("blend", 0f);
            _effectChain.Add(_vocalEffect);
        }

        // Sync initial document
        _lastDocument = getLyrics();
        Display.SetDocument(_lastDocument);

        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(50),
            DispatcherPriority.Normal,
            OnTick);
        _timer.Start();

        Closed += (_, _) =>
        {
            _timer.Stop();
            ResetVocalBlend();
        };

        KeyDown += OnKeyDown;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!_engine.IsLoaded) return;

        var pos = _engine.GetPosition();
        var ts = TimeSpan.FromSeconds(pos);
        TimeLabel.Text = $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";

        // Update display when the lyrics document changes (track change).
        var doc = _getLyrics();
        if (!ReferenceEquals(doc, _lastDocument))
        {
            _lastDocument = doc;
            Display.SetDocument(doc);
        }

        Display.SetPosition(pos);
    }

    private void OnBlendChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        var blend = (float)(e.NewValue / 100.0);
        BlendLabel.Text = $"{(int)e.NewValue}%";
        if (_vocalEffect is not null)
        {
            _vocalEffect.Parameters.Set("blend", blend);
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Space:
                _togglePlayback();
                e.Handled = true;
                break;
        }
    }

    private void ResetVocalBlend()
    {
        if (_vocalEffect is null) return;
        _vocalEffect.Parameters.Set("blend", 0f);
        _vocalEffect = null;
    }
}
