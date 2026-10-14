using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Dj;

namespace Spectralis.App.Views;

public partial class DjView : UserControl
{
    private readonly DjStemSeparationService _separation = new();
    public DjView()
    {
        InitializeComponent();
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (DataContext is DjViewModel vm) { vm.RefreshDevices(); vm.RefreshLibrary(); vm.RefreshOptions(); }
    }
    private void OnBrowseLibrary(object? sender, RoutedEventArgs e) => (DataContext as DjViewModel)?.BrowseLibrary?.Invoke();
    private async void OnCurrentA(object? sender, RoutedEventArgs e) { if (DataContext is DjViewModel { LoadNowPlaying: { } load }) await load(0); }
    private async void OnCurrentB(object? sender, RoutedEventArgs e) { if (DataContext is DjViewModel { LoadNowPlaying: { } load }) await load(1); }
    private void OnShowNowPlaying(object? sender, RoutedEventArgs e) => (DataContext as DjViewModel)?.ShowNowPlaying?.Invoke();
    private void OnRefreshDevices(object? sender, RoutedEventArgs e) => (DataContext as DjViewModel)?.RefreshDevices();
    private async void OnLibraryA(object? sender, RoutedEventArgs e) => await LoadLibrary(0);
    private async void OnLibraryB(object? sender, RoutedEventArgs e) => await LoadLibrary(1);
    private async Task LoadLibrary(int index)
    {
        if (DataContext is not DjViewModel { SelectedTrack: { } track } vm) return;
        vm.ActiveDeckIndex = index;
        await vm.ActiveDeck.LoadSourcesAsync([new Services.DjSource(track.Path, Title: track.Title, Artist: track.Artist)]);
    }
    private async void OnRecording(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DjViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        try
        {
            if (vm.IsRecording) { await vm.Mixer.StopRecordingAsync(); vm.Status = "Mix recording saved."; }
            else
            {
                var file = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Record DJ mix", SuggestedFileName = $"Spectralis mix {DateTime.Now:yyyy-MM-dd HH-mm-ss}.wav",
                    DefaultExtension = "wav", FileTypeChoices = [new("WAV audio") { Patterns = ["*.wav"] }],
                });
                if (file?.TryGetLocalPath() is not { } path) return;
                vm.EnsureOutput(); vm.Mixer.StartRecording(path); vm.Status = "Recording the master mix to " + path;
            }
            vm.Tick();
        }
        catch (Exception ex) { vm.Status = ex.Message; }
    }
    private async void OnInstallAi(object? sender, RoutedEventArgs e) => await RunAi(async (vm, progress, ct) =>
        await _separation.InstallAsync(vm.AiEnabled && vm.Enabled, progress, ct));
    private async void OnSeparateA(object? sender, RoutedEventArgs e) => await Separate(0);
    private async void OnSeparateB(object? sender, RoutedEventArgs e) => await Separate(1);
    private async Task Separate(int deck)
    {
        if (DataContext is not DjViewModel vm || TopLevel.GetTopLevel(this) is not { } top) return;
        var target = deck == 0 ? vm.DeckA : vm.DeckB;
        if (!target.CanLoad) { vm.Status = "Pause the target deck before loading new stems."; return; }
        // The service validates the runtime and model under the same lock used by install.
        // Do not use a separate UI readiness gate that can disagree after a partial install.
        var file = (await top.StorageProvider.OpenFilePickerAsync(new() { Title = "Separate a song into four stems", FileTypeFilter = [DjDeckView.AudioType] })).FirstOrDefault()?.TryGetLocalPath();
        if (file is null) return;
        await RunAi(async (model, progress, ct) =>
        {
            using var result = await _separation.SeparateAsync(file, model.AiEnabled && model.Enabled, progress, ct);
            ct.ThrowIfCancellationRequested();
            if (target.CanLoad) await target.LoadAsync(result.Stems, file);
            else model.Status = "Pause the target deck and separate again to load the generated stems.";
        });
    }
    private void OnCancelAi(object? sender, RoutedEventArgs e) => (DataContext as DjViewModel)?.AiTaskCancellation?.Cancel();
    private async void OnRemoveAi(object? sender, RoutedEventArgs e) => await RunAi(async (vm, _, _) =>
    {
        await _separation.RemoveAsync(); vm.Status = "Optional AI runtime, tools and models removed.";
    });
    private async Task RunAi(Func<DjViewModel, IProgress<string>, CancellationToken, Task> operation)
    {
        if (DataContext is not DjViewModel vm || !vm.Enabled || !vm.AiEnabled) return;
        if (vm.AiTaskCancellation is not null) { vm.Status = "An AI task is already running. Cancel it before starting another."; return; }
        using var cancellation = new CancellationTokenSource(); vm.AiTaskCancellation = cancellation;
        try { await operation(vm, new Progress<string>(message => vm.Status = message), cancellation.Token); }
        catch (OperationCanceledException) { vm.Status = "AI task cancelled. No active deck was replaced."; }
        catch (Exception ex) { vm.Status = ex.Message; }
        finally { vm.AiTaskCancellation = null; }
    }
}
