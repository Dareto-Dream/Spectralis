using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Themes.Simple;
using Avalonia.VisualTree;
using Spectralis.App.Controls;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Audio;
using Spectralis.Core.Audio.Dj;
using Spectralis.Core.Visualizers;
using Spectralis.Tests.Core;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Spectralis.Tests.App.VisualizerTestAppBuilder))]

namespace Spectralis.Tests.App;

public class VisualizerTestApplication : Application
{
    public override void Initialize()
    {
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://Spectralis.App/"))
            { Source = new Uri("avares://Spectralis.App/Design/Tokens.axaml") });
        Styles.Add(new SimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://Spectralis.App/"))
            { Source = new Uri("avares://Spectralis.App/Design/Controls.axaml") });
    }
}
public static class VisualizerTestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<VisualizerTestApplication>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

[CollectionDefinition("Visualizer UI", DisableParallelization = true)]
public sealed class VisualizerUiCollection;

[Collection("Visualizer UI")]
public sealed class PageWindowTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Fullscreen_fills_the_window_without_playback_overlays_and_restores_the_page(bool poppedOut, bool mixSource)
    {
        var path = WavFixture.CreateSineWav(2, 48000, 2);
        try
        {
            using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
            using var player = new NowPlayingViewModel(engine, enablePositionPolling: false);
            using var mix = new DjViewModel(new() { DjModeEnabled = true }, mixer: new(new FakeAudioDeviceEnumerator()), saveSettings: () => { });
            player.AttachMixer(mix);
            await player.LoadTrackAsync(path);
            if (mixSource) player.SelectMixSource();
            player.ShowVisualizer = true;
            player.ShowQueue = true;
            var view = new NowPlayingView { DataContext = player };
            Window window;
            if (poppedOut)
            {
                var page = new PageWindow("Now Playing");
                page.PageHost.Content = view;
                page.FullscreenChanged += view.SetVisualizerFullscreen;
                window = page;
            }
            else
            {
                var main = new MainWindow();
                main.FindControl<ContentControl>("PageContentHost")!.Content = view;
                window = main;
            }
            try
            {
                window.Show(); window.UpdateLayout();
                var surface = view.GetVisualDescendants().OfType<VisualizerHostControl>().Single();
                var scrim = view.GetVisualDescendants().OfType<Border>().Single(b => b.Height == 200 && b.Background is LinearGradientBrush);
                var label = view.FindControl<TextBlock>("VisualizerNameLabel")!;
                Assert.True(scrim.IsEffectivelyVisible); Assert.True(label.IsEffectivelyVisible);
                var previousSize = surface.Bounds.Size;
                view.SetVisualizerFullscreen(true); window.UpdateLayout();
                Assert.Equal(new Point(0, 0), surface.TranslatePoint(default, window));
                Assert.Equal(window.ClientSize.Width, surface.Bounds.Width, 3);
                Assert.Equal(window.ClientSize.Height, surface.Bounds.Height, 3);
                Assert.False(scrim.IsEffectivelyVisible);
                Assert.False(label.IsEffectivelyVisible);
                Assert.Equal(0, view.FindControl<Grid>("PlayerColumn")!.RowDefinitions[1].ActualHeight);
                view.SetVisualizerFullscreen(false); window.UpdateLayout();
                Assert.True(scrim.IsEffectivelyVisible); Assert.True(label.IsEffectivelyVisible);
                Assert.Equal(previousSize, surface.Bounds.Size); Assert.True(player.ShowQueue);
            }
            finally { window.Close(); }
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void Any_page_moves_without_reconstruction_and_returns_with_its_state()
    {
        var a = new object(); var b = new object(); var shell = new object();
        var first = new TextBox { Text = "Draft still here" }; var second = new TextBox { Text = "Second page" };
        var dock = new ContentControl(); var host = new Window { Content = dock };
        using var pages = new PageWindowService(dock, shell, model => ReferenceEquals(model, a) ? first : second);
        try
        {
            host.Show(); pages.Select(a, "First"); host.UpdateLayout();
            var popped = pages.PopOutCurrent()!; popped.UpdateLayout(); host.UpdateLayout();
            Assert.Same(first, popped.PageHost.Content); Assert.Same(shell, popped.DataContext); Assert.Same(a, first.DataContext);
            Assert.Same(popped, pages.PopOutCurrent()); Assert.NotSame(first, dock.Content);
            first.Text = "Edited in another window";
            pages.Select(b, "Second"); host.UpdateLayout(); Assert.Same(second, dock.Content);
            var other = pages.PopOutCurrent()!; other.UpdateLayout();
            popped.Close(); Assert.Null(pages.Find(a)); Assert.NotSame(first, dock.Content);
            pages.Select(a, "First"); host.UpdateLayout();
            Assert.Same(first, dock.Content); Assert.Equal("Edited in another window", first.Text);
            pages.Select(b, "Second"); pages.Return(b); host.UpdateLayout();
            Assert.Same(second, dock.Content); Assert.Null(other.PageHost.Content); Assert.Null(other.DataContext);
            pages.PopOutCurrent(); pages.Dispose(); Assert.Null(pages.Find(b)); Assert.Null(dock.Content);
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public async Task Shared_now_playing_popout_and_fullscreen_keep_the_same_visualizer_and_live_audio()
    {
        var path = WavFixture.CreateSineWav(2, 48000, 2);
        try
        {
            using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
            using var player = new NowPlayingViewModel(engine, enablePositionPolling: false);
            using var mix = new DjViewModel(new() { DjModeEnabled = true }, mixer: new(new FakeAudioDeviceEnumerator()), saveSettings: () => { });
            player.AttachMixer(mix);
            using var presentation = new PlaybackVisualizerSource(player, mix);
            player.VisualizationSource = presentation;
            await mix.DeckA.LoadFilesAsync([path]); mix.EnsureOutput();
            mix.Mixer.Transport(0, DjTransport.PlayPause); mix.Tick();
            Assert.True(player.IsMixSource); Assert.True(player.ShowPlaybackSourcePicker);
            Assert.False(player.HasTrack); Assert.False(player.IsNilState); Assert.True(player.HasTrackOrAlbumWorld);
            Assert.True(player.ShowVisualizerSurface); Assert.True(player.IsPlaying); Assert.Equal("Mix", player.SourceLabel);
            var view = new NowPlayingView { DataContext = player };
            var dock = new ContentControl(); var host = new Window { Content = dock };
            using var pages = new PageWindowService(dock, new object(), _ => view);
            pages.WindowOpened += w => w.FullscreenChanged += view.SetVisualizerFullscreen;
            try
            {
                host.Show(); pages.Select(player, "Now Playing"); host.UpdateLayout();
                var surface = Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(view).OfType<VisualizerHostControl>().Single();
                Assert.Same(presentation, surface.FrameSource);
                player.SelectedVisualizer = player.VisualizerOptions.First(o => o.Mode == VisualizerMode.Waveform);
                var window = pages.PopOutCurrent()!; window.UpdateLayout(); host.UpdateLayout();
                Assert.Same(view, window.PageHost.Content); Assert.Same(presentation, surface.FrameSource);
                Assert.Equal(VisualizerMode.Waveform, surface.Mode);
                view.SetVisualizerFullscreen(true);
                Assert.Equal(WindowState.FullScreen, window.WindowState); Assert.True(player.IsVisualizerFullscreen);
                Assert.False(player.ShowPlaybackSourcePicker); Assert.Equal(8, Grid.GetColumnSpan(view.FindControl<Grid>("PlayerColumn")!));
                window.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
                Assert.Equal(WindowState.Normal, window.WindowState); Assert.False(player.IsVisualizerFullscreen);
                Assert.Equal(1, Grid.GetColumnSpan(view.FindControl<Grid>("PlayerColumn")!));
                view.SetVisualizerFullscreen(true); window.Close(); host.UpdateLayout();
                Assert.False(player.IsVisualizerFullscreen); Assert.Same(view, dock.Content);
                Assert.Same(presentation, surface.FrameSource); Assert.True(mix.Mixer.GetDeck(0).Playing);
                var samples = new float[9600]; mix.Mixer.Read(samples, 0, samples.Length);
                Assert.Contains(samples, s => Math.Abs(s) > .01);
                Assert.Same(presentation.GetFrame(), presentation.GetFrame());
                player.VolumePercent = 42; Assert.Equal(42, mix.MasterVolume);
                player.TogglePlayback(); Assert.False(mix.IsPlaying);
                player.TogglePlayback(); Assert.True(mix.IsPlaying);
                player.SelectedPlaybackSource = "Track"; Assert.False(mix.IsPlaying); Assert.False(presentation.UsesMix);
                player.SelectedPlaybackSource = "Mix"; Assert.True(player.IsMixSource);
                await player.LoadTrackAsync(path);
                Assert.False(player.IsMixSource); Assert.True(engine.IsLoaded); Assert.False(mix.IsPlaying);
                player.SelectedPlaybackSource = "Mix"; Assert.True(player.IsMixSource); Assert.False(engine.IsPlaying);
                await mix.SetEnabledAsync(false);
                Assert.False(player.IsMixSource); Assert.False(player.ShowPlaybackSourcePicker); Assert.Equal(new[] { "Track" }, player.PlaybackSources);
                player.SelectedPlaybackSource = "Mix"; Assert.False(player.IsMixSource);
            }
            finally { host.Close(); }
        }
        finally { File.Delete(path); }
    }
}
