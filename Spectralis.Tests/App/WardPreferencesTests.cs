using System.Text.Json;
using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class WardPreferencesTests
{
    [Fact]
    public void WardSyncOnlyCopiesPortableSettings()
    {
        var source = new AppSettings { ThemeMode = AppThemeMode.Oled, CrossfadeSeconds = 4, SqOwnerToken = "secret", EnableAutoUpdates = false };
        var preferences = WardPreferences.Capture(source);
        var json = JsonSerializer.Serialize(preferences);
        Assert.DoesNotContain("secret", json);
        Assert.DoesNotContain("AutoUpdates", json);
        var target = new AppSettings { SqOwnerToken = "local", EnableAutoUpdates = true };
        preferences.Apply(target);
        Assert.Equal(AppThemeMode.Oled, target.ThemeMode);
        Assert.Equal(4, target.CrossfadeSeconds);
        Assert.Equal("local", target.SqOwnerToken);
        Assert.True(target.EnableAutoUpdates);
        Assert.Throws<InvalidOperationException>(() => (preferences with { SchemaVersion = 2 }).Apply(target));
    }
}
