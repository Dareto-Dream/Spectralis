using System.Text.Json;
using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class VersionHoldTests
{
    // Settings understood by releases before version selection was added.
    private sealed class LegacySettings
    {
        public bool EnableAutoUpdates { get; set; } = true;
        public string? IgnoredUpdateVersion { get; set; }
    }

    [Fact]
    public void DowngradingPreventsAnOlderAppFromImmediatelyUpdatingBack()
    {
        var settings = new AppSettings { EnableAutoUpdates = true };
        settings.HoldVersion("7.1.0", "7.1.1");
        var legacy = JsonSerializer.Deserialize<LegacySettings>(JsonSerializer.Serialize(settings))!;

        Assert.Equal("7.1.0", settings.HeldVersion);
        Assert.False(legacy.EnableAutoUpdates);
        Assert.Equal("7.1.1", legacy.IgnoredUpdateVersion);
        Assert.Equal("7.1.0", settings.Clone().HeldVersion);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SelectingANewerBuildPreservesTheAutomaticUpdatePreference(bool autoUpdates)
    {
        var settings = new AppSettings { EnableAutoUpdates = autoUpdates };
        settings.HoldVersion("7.1.2-beta.1", "7.1.1");

        Assert.Equal("7.1.2-beta.1", settings.HeldVersion);
        Assert.Equal(autoUpdates, settings.EnableAutoUpdates);
    }
}
