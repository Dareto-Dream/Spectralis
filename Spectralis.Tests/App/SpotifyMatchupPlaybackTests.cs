using System.Text.Json;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio;
using Spectralis.Tests.Core;
using Xunit;

namespace Spectralis.Tests.App;

// The feature switch is shared by the app, so don't change it under another playback test.
[CollectionDefinition("Spotify matchup", DisableParallelization = true)]
public class SpotifyMatchupCollection { }

[Collection("Spotify matchup")]
public sealed class SpotifyMatchupPlaybackTests
{
    private const string TrackId = "4cOdK2wGLETKBW3PvgPWqT";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedMatchUsesLocalPlaybackAndSurvivesTrackChanges(bool startPlayback)
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), "spectralis-matchup-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheRoot);
        var fixture = WavFixture.CreateSineWav(1);
        var audio = Path.Combine(cacheRoot, TrackId + ".wav");
        var wasEnabled = BetaFeatures.SpotifyMatchup;
        try
        {
            File.Copy(fixture, audio);
            var match = new SpotifyMatchupService.CachedMatch(TrackId, "dQw4w9WgXcQ", Path.GetFileName(audio),
                "Spotify title", "Spotify artist", "Spotify album", null, 1000, DateTime.UtcNow);
            await File.WriteAllTextAsync(Path.Combine(cacheRoot, "index.json"), JsonSerializer.Serialize(new[] { match }));
            BetaFeatures.Apply(new AppSettings { UpdateChannel = "beta", SpotifyMatchupEnabled = true });
            using var engine = new AudioEngine(new FakeAudioDeviceEnumerator());
            using var vm = new NowPlayingViewModel(engine, enablePositionPolling: false,
                spotifyMatchup: new SpotifyMatchupService(cacheRoot));

            await vm.PlayQueueAsync([$"spotify:track:{TrackId}"], 0, startPlayback);

            Assert.Empty(vm.LoadError);
            Assert.True(vm.HasTrack);
            Assert.Equal(startPlayback, vm.IsPlaying);
            Assert.Equal(audio, engine.CurrentTrack?.SourcePath);
            Assert.Equal("Spotify title", vm.Title);
            Assert.Equal("Spotify artist", vm.Artist);

            await vm.LoadTrackAsync(fixture);
            Assert.True(File.Exists(audio));
            // A replay is served from the index without a Spotify account, SDK host or network lookup.
            var replay = await new SpotifyMatchupService(cacheRoot).ResolveAsync(TrackId, null, null, CancellationToken.None);
            Assert.Equal(audio, replay?.CachedAudioPath);
        }
        finally
        {
            BetaFeatures.Apply(new AppSettings { UpdateChannel = "beta", SpotifyMatchupEnabled = wasEnabled });
            File.Delete(fixture);
            foreach (var file in Directory.EnumerateFiles(cacheRoot)) File.Delete(file);
            Directory.Delete(cacheRoot);
        }
    }
}
