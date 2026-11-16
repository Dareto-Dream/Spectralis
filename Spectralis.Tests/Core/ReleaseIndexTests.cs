using Xunit;
using Spectralis.Core.Platform;

namespace Spectralis.Tests.Core;

public class ReleaseIndexTests
{
    private const string Base = "https://github.com/Dareto-Dream/Spectralis/releases/download";

    [Theory]
    [InlineData("7.1.0", "7.1.1", -1)]
    [InlineData("7.1.1-beta.4", "7.1.1", -1)]
    [InlineData("7.1.1-beta.4", "7.1.1-beta.10", -1)]
    [InlineData("7.1.1-beta.4", "7.1.0", 1)]
    [InlineData("7.10.0", "7.9.9", 1)]
    [InlineData("v7.1.0", "7.1.0", 0)]
    [InlineData("7.1.0.0", "7.1.0", 0)]
    [InlineData("7.1.0+abc123", "7.1.0", 0)]
    [InlineData("garbage", "7.1.0", -1)]
    public void VersionsOrderTheWaySemverSays(string a, string b, int expected) =>
        Assert.Equal(expected, Math.Sign(ReleaseVersion.Compare(a, b)));

    [Fact]
    public void UpdateSelectionRespectsOptInInstalledVersionAndTheCdnOffer()
    {
        static ReleaseInfo Row(string v, bool beta = false) => new(v, beta, "", Base + "/v" + v, "", "");
        var releases = new[] { Row("7.1.0"), Row("7.1.1-beta.2", true), Row("7.1.1-beta.10", true) };
        Assert.Null(ReleaseIndex.NewestUpdate(releases, "7.1.0", includeBetas: false));
        Assert.Equal("7.1.1-beta.10", ReleaseIndex.NewestUpdate(releases, "7.1.0", includeBetas: true)?.Version);
        Assert.Null(ReleaseIndex.NewestUpdate(releases, "7.1.1-beta.10", includeBetas: true));
        Assert.Null(ReleaseIndex.NewestUpdate(releases, "7.1.0", includeBetas: true, offeredVersion: "7.1.1"));
        var withStable = releases.Append(Row("7.1.1"));
        Assert.Equal("7.1.1", ReleaseIndex.NewestUpdate(withStable, "7.1.1-beta.10", includeBetas: true)?.Version);
        Assert.Equal("7.1.1", ReleaseIndex.NewestUpdate(withStable, "7.1.0", includeBetas: false)?.Version);
    }

    [Fact]
    public void PrereleasesAreRecognised()
    {
        Assert.True(ReleaseVersion.IsPrerelease("7.1.1-beta.4"));
        Assert.False(ReleaseVersion.IsPrerelease("7.1.1"));
        Assert.False(ReleaseVersion.IsPrerelease("7.1.1-"));
    }

    [Theory]
    [InlineData(Base + "/v7.1.1", true)]
    [InlineData("https://spectralis-cdn.deltavdevs.com", true)]
    [InlineData("http://github.com/Dareto-Dream/Spectralis/releases/download/v7.1.1", false)]
    [InlineData("https://github.com/someone-else/Spectralis/releases/download/v7.1.1", false)]
    [InlineData("https://github.com/Dareto-Dream/Spectralis/releases/download/../../../evil/x", false)]
    [InlineData("https://github.com.evil.example/Dareto-Dream/Spectralis/releases/download/v1", false)]
    [InlineData("https://user@github.com/Dareto-Dream/Spectralis/releases/download/v7.1.1", false)]
    [InlineData("", false)]
    public void OnlyOurOwnFeedsAreFollowed(string feedBase, bool trusted) =>
        Assert.Equal(trusted, ReleaseIndex.IsTrustedFeedBase(feedBase));

    [Fact]
    public void TheIndexIsFilteredToThisPlatformAndSortedNewestFirst()
    {
        var json = $$"""
        [
          { "version": "7.1.0", "channel": "stable", "date": "2026-11-01", "feedBase": "{{Base}}/v7.1.0",
            "notesUrl": "https://github.com/Dareto-Dream/Spectralis/releases/tag/v7.1.0",
            "platforms": { "win-x64": { "installer": "{{Base}}/v7.1.0/Spectralis-win-x64-Setup.exe" }, "linux-x64": { "installer": "" } } },
          { "version": "7.1.1-beta.2", "channel": "beta", "feedBase": "{{Base}}/v7.1.1-beta.2",
            "platforms": { "win-x64": { "installer": "" } } },
          { "version": "7.1.1-beta.10", "channel": "beta", "feedBase": "{{Base}}/v7.1.1-beta.10",
            "platforms": { "win-x64": { "installer": "" } } },
          { "version": "6.0.0", "feedBase": "{{Base}}/v6.0.0", "platforms": { "osx-arm64": { "installer": "" } } },
          { "version": "9.9.9", "feedBase": "https://evil.example/feed", "platforms": { "win-x64": { "installer": "" } } },
          { "version": "nope", "feedBase": "{{Base}}/vnope", "platforms": { "win-x64": {} } },
          "junk"
        ]
        """;

        var windows = ReleaseIndex.Parse(json, "win-x64");
        Assert.Equal(["7.1.1-beta.10", "7.1.1-beta.2", "7.1.0"], windows.Select(r => r.Version));
        Assert.Equal("7.1.1-beta.10", ReleaseIndex.NewestBeta(windows)!.Version);
        Assert.True(windows[0].IsBeta);
        Assert.False(windows[2].IsBeta);
        Assert.Equal($"{Base}/v7.1.0/Spectralis-win-x64-Setup.exe", windows[2].InstallerUrl);

        Assert.Equal(["7.1.0"], ReleaseIndex.Parse(json, "linux-x64").Select(r => r.Version));
        Assert.Null(ReleaseIndex.NewestBeta(ReleaseIndex.Parse(json, "linux-x64")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"version\":\"7.1.0\"}")]
    public void ABrokenIndexIsAnEmptyListNotACrash(string json) =>
        Assert.Empty(ReleaseIndex.Parse(json, "win-x64"));
}
