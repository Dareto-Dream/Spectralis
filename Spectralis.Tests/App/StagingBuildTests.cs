using Spectralis.App.Services;
using Spectralis.Core.Platform;
using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.App;

public class StagingBuildTests
{
    [Fact]
    public void Build_target_controls_backend_and_settings_isolation()
    {
        var settings = AppSettingsStore.Normalize(new AppSettings { SharedPlayCdnBaseUrl = SpectralisEndpoints.ProductionApiBase,
            SqCdnBaseUrl = SpectralisEndpoints.ProductionApiBase, EnableAutoUpdates = true });
#if SPECTRALIS_STAGING
        Assert.True(SpectralisEndpoints.IsStaging);
        Assert.Equal(SharedPlayDefaults.StagingCdnBaseUrl, SpectralisEndpoints.ApiBase);
        Assert.EndsWith("settings-staging.json", AppSettingsStore.SettingsPath);
        Assert.Equal(SharedPlayDefaults.StagingCdnBaseUrl, settings.SharedPlayCdnBaseUrl);
        Assert.Equal(SharedPlayDefaults.StagingCdnBaseUrl, settings.SqCdnBaseUrl);
        Assert.False(settings.EnableAutoUpdates);
        Assert.True(BetaFeatures.Available(settings));
        Assert.Equal(SharedPlayDefaults.StagingCdnBaseUrl, SharedPlayDefaults.NormalizeCdnBaseUrl(SpectralisEndpoints.ProductionApiBase));
        Assert.False(SharedPlayDefaults.SupportsWard(new Uri(SpectralisEndpoints.ApiBase)));
#else
        Assert.False(SpectralisEndpoints.IsStaging);
        Assert.Equal(SpectralisEndpoints.ProductionApiBase, SpectralisEndpoints.ApiBase);
        Assert.EndsWith("settings-avalonia.json", AppSettingsStore.SettingsPath);
        Assert.True(settings.EnableAutoUpdates);
#endif
    }

    [Fact]
    public void Staging_room_links_stay_on_staging()
    {
        var backend = new Uri(SharedPlayDefaults.StagingCdnBaseUrl);
        Assert.Equal(backend.Host, SharedPlayDefaults.BuildWebShareJoinUrl(backend, "ABC123").Host);
        Assert.Equal(backend.Host, SharedPlayDefaults.BuildRoomJoinUrl(backend, "room").Host);
    }
}
