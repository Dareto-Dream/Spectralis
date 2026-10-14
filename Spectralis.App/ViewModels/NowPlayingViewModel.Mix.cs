using System.ComponentModel;
using ReactiveUI;

namespace Spectralis.App.ViewModels;

public sealed partial class NowPlayingViewModel
{
    private DjViewModel? _mixSource;
    private static readonly string[] TrackSources = ["Track"], MixSources = ["Track", "Mix"];
    private bool _isVisualizerFullscreen;
    private (bool Visualizer, bool Video, bool Html)? _trackSurface;
    private string? _lastMixTitle;
    private bool _lastMixPlaying;
    private double _lastMixVolume;
    public bool IsVisualizerFullscreen { get => _isVisualizerFullscreen; set { this.RaiseAndSetIfChanged(ref _isVisualizerFullscreen, value); this.RaisePropertyChanged(nameof(ShowPlaybackSourcePicker)); } }
    public bool ShowPlaybackSourcePicker => DjModeEnabled && !IsVisualizerFullscreen;
    public Action? OpenDjTools { get; private set; }
    public bool IsMixSource => _mixSource is { Enabled: true, UseForVisualization: true };
    public bool IsTrackSource => !IsMixSource;
    public IReadOnlyList<string> PlaybackSources => _mixSource?.Enabled == true ? MixSources : TrackSources;
    public string SelectedPlaybackSource { get => IsMixSource ? "Mix" : "Track"; set { if (value == "Mix" && !IsMixSource) SelectMixSource(); else if (value == "Track") SelectTrackSource(); } }
    public void AttachMixer(DjViewModel mixer, Action? openTools = null)
    {
        if (_mixSource is not null) _mixSource.PropertyChanged -= OnMixSourceChanged;
        _mixSource = mixer; OpenDjTools = openTools; mixer.PropertyChanged += OnMixSourceChanged;
        RefreshMixSource();
    }
    public void SelectMixSource()
    {
        if (_mixSource is not { Enabled: true } mix) return;
        PauseTrackSource();
        mix.UseForVisualization = true;
    }
    public void PauseTrackSource()
    {
        if (_spotifyState is { IsPaused: false } && _spotifyHost is not null) _ = _spotifyHost.PauseAsync();
        if (_engine.IsPlaying) _engine.Pause();
        // Browser-only audio must yield the speaker to the mix as well.
        if (IsWidgetFallbackActive) ShowEmbeddedHtml = false;
    }
    public void SelectTrackSource()
    {
        if (_mixSource is null || !_mixSource.UseForVisualization) return;
        _mixSource.Mixer.PauseAll(); _mixSource.UseForVisualization = false; _mixSource.Tick();
    }
    private void OnMixSourceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DjViewModel.UseForVisualization) or nameof(DjViewModel.Enabled))
        {
            if (IsMixSource)
            {
                _trackSurface ??= (ShowVisualizer, ShowYouTubeVideo, ShowEmbeddedHtml);
                PauseTrackSource(); ShowYouTubeVideo = false; ShowEmbeddedHtml = false; ShowVisualizer = true;
            }
            else if (_trackSurface is { } surface)
            {
                _trackSurface = null;
                ShowVisualizer = surface.Visualizer; ShowYouTubeVideo = surface.Video; ShowEmbeddedHtml = surface.Html;
            }
            RaiseSurfaceModeChanged();
            RefreshMixSource();
        }
        else if (IsMixSource && _mixSource is { } mix)
        {
            // Deck meters refresh at 20 Hz. Keep their ticks out of the source picker
            // and the native media surfaces, which only need actual state changes.
            if (e.PropertyName == nameof(DjViewModel.SessionTitle) && _lastMixTitle != mix.SessionTitle)
            { _lastMixTitle = mix.SessionTitle; this.RaisePropertyChanged(nameof(Title)); }
            if (e.PropertyName == nameof(DjViewModel.IsPlaying) && _lastMixPlaying != mix.IsPlaying)
            {
                _lastMixPlaying = mix.IsPlaying;
                foreach (var property in new[] { nameof(IsPlaying), nameof(PlayPauseIconData), nameof(PlayPauseMenuLabel) }) this.RaisePropertyChanged(property);
            }
            if (e.PropertyName == nameof(DjViewModel.MasterVolume) && _lastMixVolume != mix.MasterVolume)
            {
                _lastMixVolume = mix.MasterVolume;
                foreach (var property in new[] { nameof(VolumePercent), nameof(IsMuted), nameof(MuteMenuLabel), nameof(VolumeLabel) }) this.RaisePropertyChanged(property);
            }
        }
    }
    private void RefreshMixSource()
    {
        _lastMixTitle = _mixSource?.SessionTitle; _lastMixPlaying = _mixSource?.IsPlaying ?? false; _lastMixVolume = _mixSource?.MasterVolume ?? 0;
        foreach (var property in new[] { nameof(IsMixSource), nameof(IsTrackSource), nameof(PlaybackSources), nameof(SelectedPlaybackSource),
            nameof(DjModeEnabled), nameof(ShowPlaybackSourcePicker), nameof(IsNilState), nameof(HasTrackOrAlbumWorld), nameof(Title), nameof(Artist), nameof(Album),
            nameof(FormatBadge), nameof(CoverArtBytes), nameof(SourceLabel), nameof(IsPlaying), nameof(PlayPauseIconData),
            nameof(PlayPauseMenuLabel), nameof(VolumePercent), nameof(IsMuted), nameof(MuteMenuLabel), nameof(VolumeLabel),
            nameof(ShowVisualizerSurface), nameof(ShowArtworkSurface), nameof(ShowVisualizerControls) }) this.RaisePropertyChanged(property);
    }
}
