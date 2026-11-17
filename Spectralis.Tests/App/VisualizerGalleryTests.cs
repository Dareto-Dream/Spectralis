using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.App.Views;
using Spectralis.Core.Visualizers;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("Visualizer UI")]
public sealed class VisualizerGalleryTests
{
    private static VisualizerOption[] Options() => VisualizerCatalog.All.Select(d => new VisualizerOption(d.Label, d.Mode)).ToArray();

    [Fact]
    public void Families_preserve_every_visualizer_once_and_find_variations_by_name()
    {
        var options = Options();
        var vm = new VisualizerGalleryViewModel(options, options.Single(o => o.Mode == VisualizerMode.RadialSpectrum));
        Assert.Equal("Spectrum", vm.SelectedFamily!.Name);
        Assert.Equal(VisualizerMode.RadialSpectrum, vm.PreviewMode);
        Assert.Equal(options.Length, vm.Families.SelectMany(f => f.Variants).Distinct().Count());
        Assert.Equal(options.Length, vm.Families.Sum(f => f.Variants.Count));
        vm.Search = "radial";
        Assert.Single(vm.Families);
        vm.Category = "Artwork";
        Assert.True(vm.IsEmpty);
        vm.Search = "";
        Assert.Single(vm.Families);
        Assert.Equal("Artwork", vm.SelectedFamily.Name);
        vm.Category = "All";
        Assert.False(vm.IsEmpty);
    }

    [Fact]
    public void Preferences_round_trip_and_normalize_retired_modes()
    {
        var original = new AppSettings
        {
            CurrentVisualizer = VisualizerMode.GpuTunnel, DefaultVisualizer = VisualizerMode.GpuRadial,
            EnableInterfaceAnimations = false,
            VisualizerAppearance = new() { Colorway = VisualizerColorway.Glacier, Detail = VisualizerDetail.Low, MotionPercent = 180, ReduceMotion = true, ShowHud = false },
        };
        var json = System.Text.Json.JsonSerializer.Serialize(original.Clone());
        var restored = AppSettingsStore.Normalize(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!);
        Assert.Equal(VisualizerMode.Wormhole, restored.CurrentVisualizer);
        Assert.Equal(VisualizerMode.RadialSpectrum, restored.DefaultVisualizer);
        Assert.Equal(original.VisualizerAppearance, restored.VisualizerAppearance);
        Assert.False(restored.EnableInterfaceAnimations);
        var malformed = AppSettingsStore.Normalize(new AppSettings { VisualizerAppearance = null! });
        Assert.NotNull(malformed.VisualizerAppearance);
    }

    [Fact]
    public void Backdating_keeps_settings_readable_by_apps_without_the_new_modes()
    {
        var settings = new AppSettings { CurrentVisualizer = VisualizerMode.Wormhole, DefaultVisualizer = VisualizerMode.Silk };
        settings.HoldVersion("8.0.0", "8.1.0");
        var serialized = System.Text.Json.JsonSerializer.Serialize(settings);
        Assert.Equal(VisualizerMode.MirrorSpectrum, settings.CurrentVisualizer);
        Assert.Equal(VisualizerMode.MirrorSpectrum, settings.DefaultVisualizer);
        Assert.False(settings.EnableAutoUpdates);
        Assert.Equal("8.1.0", settings.IgnoredUpdateVersion);
        Assert.DoesNotContain("Wormhole", serialized);
    }

    [Fact]
    public void Starfield_migrates_and_backdating_keeps_new_modes_out_of_older_settings()
    {
        var settings=AppSettingsStore.Normalize(new AppSettings{CurrentVisualizer=VisualizerMode.Starfield,DefaultVisualizer=VisualizerMode.SignalCity});
        Assert.Equal(VisualizerMode.Stardust,settings.CurrentVisualizer);
        settings.HoldVersion("8.1.0","8.2.0");
        Assert.Equal(VisualizerMode.Starfield,settings.CurrentVisualizer);
        Assert.Equal(VisualizerMode.Graph3D,settings.DefaultVisualizer);
        Assert.False(settings.EnableAutoUpdates);
        var gallery=new VisualizerGalleryViewModel(Options());
        Assert.DoesNotContain(gallery.Families,f=>f.Name=="Starfield");
        Assert.Contains(gallery.Families,f=>f.Name=="Stardust");
    }

    [AvaloniaTheory]
    [InlineData(720)]
    [InlineData(980)]
    public void Gallery_keeps_the_preview_and_apply_action_inside_the_window(int width)
    {
        var vm = new VisualizerGalleryViewModel(Options());
        var window = new VisualizerPickerWindow { Width = width, Height = 620, DataContext = vm };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Null(window.FindControl<Spectralis.App.Controls.VisualizerHostControl>("Preview"));
            var button = window.FindControl<Button>("ApplyButton")!;
            var position = button.TranslatePoint(default, window)!.Value;
            Assert.InRange(position.X + button.Bounds.Width, 0, width);
            Assert.InRange(position.Y + button.Bounds.Height, 0, 620);
            window.FindControl<TextBox>("SearchBox")!.Text = "no such visualizer";
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.IsEnabled);
            vm.Search = "silk";
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsEnabled);
            Assert.Equal(VisualizerMode.Silk, vm.PreviewMode);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Built_in_grid_tiles_have_packaged_renderer_screenshots()
    {
        Span<byte> header=stackalloc byte[24];
        foreach(var definition in VisualizerCatalog.All)
        {
            var uri=new Uri($"avares://Spectralis.App/Assets/VisualizerCaptures/{definition.Mode}.png");
            Assert.True(Avalonia.Platform.AssetLoader.Exists(uri),definition.Label);
            using var stream=Avalonia.Platform.AssetLoader.Open(uri);
            // The headless platform uses a dummy 1x1 bitmap decoder. Verify
            // the embedded PNG header directly, rather than testing that stub.
            stream.ReadExactly(header);
            Assert.Equal(640,System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[16..20]));
            Assert.Equal(360,System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
        }
    }

    [AvaloniaFact]
    public async Task Applying_a_variation_returns_that_choice_and_cancel_leaves_it_alone()
    {
        var options = Options();
        var owner = new Window();
        owner.Show();
        try
        {
            var task = VisualizerPickerWindow.ChooseAsync(owner, options, options[0]);
            var picker = owner.OwnedWindows.OfType<VisualizerPickerWindow>().Single();
            var gallery = (VisualizerGalleryViewModel)picker.DataContext!;
            gallery.SelectedFamily = gallery.Families.Single(f => f.Name == "Spectrum");
            gallery.SelectedVariant = gallery.SelectedFamily.Variants.Single(v => v.Mode == VisualizerMode.RadialSpectrum);
            picker.FindControl<Button>("ApplyButton")!.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(VisualizerMode.RadialSpectrum, (await task)!.Mode);
            var canceled = VisualizerPickerWindow.ChooseAsync(owner, options, options[0]);
            owner.OwnedWindows.OfType<VisualizerPickerWindow>().Single().Close();
            Assert.Null(await canceled);
        }
        finally { owner.Close(); }
    }

}
