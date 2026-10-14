using NAudio.Wave;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Dj;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class DjWorkflowTests
{
    [Fact]
    public async Task Mix_transport_resumes_only_previously_playing_decks_and_ignores_empty_decks()
    {
        var path = WavFixture.CreateSineWav(2, 48000, 2);
        try
        {
            var interruptions = 0;
            using var vm = new DjViewModel(new() { DjModeEnabled = true }, beforePlay: () => interruptions++,
                mixer: new(new FakeAudioDeviceEnumerator()), saveSettings: () => { });
            vm.TogglePlayback(); Assert.Equal(0, interruptions);
            await vm.DeckA.LoadFilesAsync([path]); await vm.DeckB.LoadFilesAsync([path]);
            vm.EnsureOutput(); vm.Mixer.Transport(1, DjTransport.PlayPause); vm.Tick();
            vm.TogglePlayback(); Assert.False(vm.IsPlaying);
            vm.TogglePlayback(); Assert.True(vm.DeckB.IsPlaying); Assert.False(vm.DeckA.IsPlaying);
            vm.MasterVolume = 63; vm.ToggleMute(); Assert.Equal(0, vm.MasterVolume);
            vm.ToggleMute(); Assert.Equal(63, vm.MasterVolume);
            vm.StopPlayback(); Assert.False(vm.IsPlaying); Assert.Equal(0, vm.DeckB.Position);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task Dj_and_ai_default_off_and_reject_playback_and_installation_without_opt_in()
    {
        var settings = new AppSettings();
        Assert.False(settings.DjModeEnabled); Assert.False(settings.DjAiStemSeparationEnabled);
        using var vm = new DjViewModel(settings, mixer: new(new FakeAudioDeviceEnumerator()), saveSettings: () => { });
        Assert.False(vm.Enabled); Assert.False(vm.AiEnabled);
        Assert.Throws<InvalidOperationException>(vm.EnsureOutput);
        var ai = new DjStemSeparationService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.InstallAsync(false, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ai.SeparateAsync("never-open-this.m4a", false, null, default));
        var clone = new AppSettings { DjModeEnabled = true, DjAiStemSeparationEnabled = true, DjMasterOutputDeviceId = "master", DjCueOutputDeviceId = "cue" }.Clone();
        Assert.True(clone.DjModeEnabled); Assert.True(clone.DjAiStemSeparationEnabled);
        Assert.Equal("master", clone.DjMasterOutputDeviceId); Assert.Equal("cue", clone.DjCueOutputDeviceId);
    }

    [Fact]
    public async Task Disabling_mode_stops_devices_unloads_stems_cancels_ai_and_finalizes_recording()
    {
        var path = WavFixture.CreateSineWav(2, 48000, 2);
        var directory = Path.Combine(Path.GetTempPath(), "spectralis-dj-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var devices = new FakeAudioDeviceEnumerator();
            var interrupted = false;
            using var vm = new DjViewModel(new() { DjModeEnabled = true }, beforePlay: () => interrupted = true,
                mixer: new(devices), cues: new(Path.Combine(directory, "cues.json")), saveSettings: () => { });
            await vm.DeckA.LoadAsync([new(path, "Song")]);
            Assert.True(vm.DeckA.HasTrack); Assert.Single(vm.DeckA.Stems);
            using var aiCancellation = new CancellationTokenSource(); vm.AiTaskCancellation = aiCancellation;
            vm.EnsureOutput(); Assert.True(interrupted);
            vm.Mixer.Transport(0, DjTransport.PlayPause);
            var recording = Path.Combine(directory, "mix.wav"); vm.Mixer.StartRecording(recording);
            devices.Current!.Pump(4800);
            await vm.SetEnabledAsync(false);
            Assert.False(vm.Enabled); Assert.True(aiCancellation.IsCancellationRequested);
            Assert.False(vm.DeckA.HasTrack); Assert.Empty(vm.DeckA.Stems); Assert.Equal("No track loaded", vm.DeckA.Title);
            Assert.False(vm.IsRecording); Assert.True(devices.Current.IsDisposed);
            using var reader = new WaveFileReader(recording); Assert.True(reader.Length > 0);
            await vm.SetEnabledAsync(true); Assert.True(vm.Enabled);
            vm.AiTaskCancellation = null;
        }
        finally { File.Delete(path); Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Cue_points_and_manual_grid_restore_and_changed_source_invalidates_them()
    {
        var path = WavFixture.CreateSineWav(3, 48000, 2);
        var directory = Path.Combine(Path.GetTempPath(), "spectralis-cues-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var cuePath = Path.Combine(directory, "cues.json");
            using var mixer = new DjMixerEngine(new FakeAudioDeviceEnumerator());
            mixer.LoadDeck(0, await DjTrack.LoadAsync([new(path, "Original")]));
            mixer.ConfigureDeck(0, new(Bpm: 120, BeatOffset: .1));
            mixer.Seek(0, .6); mixer.HotCue(0, 0); mixer.Transport(0, DjTransport.SetCue);
            new DjCueStore(cuePath).Save(mixer.GetDeck(0));
            var saved = new DjCueStore(cuePath).Get(path);
            Assert.NotNull(saved); Assert.Equal(.6, saved.Cue, 6); Assert.Equal(.6, saved.HotCues[0]!.Value, 6);
            Assert.Equal(120, saved.Bpm); Assert.Equal(.1, saved.BeatOffset);
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(5));
            Assert.Null(new DjCueStore(cuePath).Get(path));
        }
        finally { File.Delete(path); Directory.Delete(directory, true); }
    }
}
