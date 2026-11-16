using Spectralis.App.Services;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class RecoveryUpdaterTests
{
    [Fact]
    public async Task DecliningTheWarningDoesNotPrepareDownloadOrChangePreferences()
    {
        var settings = new AppSettings();
        await VersionInstallFlow.RunAsync("5.3.0", "7.1.2", () => Task.FromResult(false),
            () => Assert.Fail("prepared recovery after cancel"), _ => throw new Exception("started download after cancel"),
            () => settings.HoldVersion("5.3.0", "7.1.2"));
        Assert.True(settings.EnableAutoUpdates);
        Assert.Empty(settings.HeldVersion);
    }

    [Theory]
    [InlineData("5.0.5")]
    [InlineData("5.3.0")]
    [InlineData("7.1.0")]
    public async Task WarnsAndPreparesRecoveryBeforeDownloadingButDisablesUpdatesOnlyBeforeRestart(string version)
    {
        var settings = new AppSettings();
        var events = new List<string>();
        await VersionInstallFlow.RunAsync(version, "7.1.2", () => { events.Add("warn"); return Task.FromResult(true); },
            () => events.Add("recovery"), ready =>
            {
                events.Add("download");
                Assert.True(settings.EnableAutoUpdates);
                ready();
                Assert.False(settings.EnableAutoUpdates);
                return Task.CompletedTask;
            }, () => { events.Add("preferences"); settings.HoldVersion(version, "7.1.2"); });
        Assert.Equal(new[] { "warn", "recovery", "download", "preferences" }, events);
        Assert.Equal(version, settings.HeldVersion);
    }

    [Fact]
    public async Task FailedRecoveryPreparationPreventsDowngrade()
    {
        await Assert.ThrowsAsync<IOException>(() => VersionInstallFlow.RunAsync("5.3.0", "7.1.2",
            () => Task.FromResult(true), () => throw new IOException("read-only user directory"),
            _ => throw new Exception("download must not start"), () => Assert.Fail("preferences changed")));
    }

    [Fact]
    public async Task CanceledDownloadKeepsAutomaticUpdatesEnabled()
    {
        var settings = new AppSettings();
        await VersionInstallFlow.RunAsync("5.3.0", "7.1.2", () => Task.FromResult(true), () => { },
            _ => Task.CompletedTask, () => settings.HoldVersion("5.3.0", "7.1.2"));
        Assert.True(settings.EnableAutoUpdates);
        Assert.Empty(settings.HeldVersion);
    }

    [Fact]
    public void RecoveryBinarySurvivesReplacingTheVersionedAppDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "spectralis-recovery-test-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "current", "Updater");
        var persistent = Path.Combine(root, "user-data", "Updater");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, RecoveryUpdater.FileName), "test updater");
            var installed = RecoveryUpdater.EnsureInstalled(persistent, source, createShortcut: false);
            Directory.Delete(Path.Combine(root, "current"), recursive: true);
            Assert.Equal("test updater", File.ReadAllText(installed));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
