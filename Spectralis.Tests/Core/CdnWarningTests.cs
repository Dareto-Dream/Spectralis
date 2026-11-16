using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class CdnWarningTests
{
    [Theory]
    [InlineData("5.0.0", true)]
    [InlineData("5.0.0.0", true)]
    [InlineData("5.0.0-beta.1", false)]
    [InlineData("7.1.2-beta.1", false)]
    [InlineData("7.1.2", false)]
    [InlineData("unknown", false)]
    [InlineData(null, false)]
    public void LegacyUpdaterWarningOnlyAppliesToItsTargetVersion(string? current, bool expected)
    {
        var warning = new CdnWarning { Versions = ["5.0.0"] };
        Assert.Equal(expected, warning.AppliesToVersion(current));
    }

    [Theory]
    [InlineData("7.1.2-beta.1", false)]
    [InlineData("7.1.2-beta.2", true)]
    [InlineData("7.1.2-beta.10", true)]
    [InlineData("7.1.2-beta.11", false)]
    [InlineData("7.1.2", false)]
    public void VersionBoundsIncludeOnlyTheAffectedBetaBuilds(string current, bool expected)
    {
        var warning = new CdnWarning { MinVersion = "7.1.2-beta.2", MaxVersion = "7.1.2-beta.10" };
        Assert.Equal(expected, warning.AppliesToVersion(current));
    }

    [Theory]
    [InlineData("7.1.2-beta.2+build123", true)]
    [InlineData("7.1.2-beta.3", false)]
    [InlineData("7.1.2", false)]
    public void ExactBetaTargetsTakePriorityOverBounds(string current, bool expected)
    {
        var warning = new CdnWarning { Versions = ["7.1.2-beta.2"], MinVersion = "9.0.0" };
        Assert.Equal(expected, warning.AppliesToVersion(current));
    }

    [Theory]
    [InlineData("7.1.2-beta.1")]
    [InlineData("7.1.2")]
    [InlineData("unknown")]
    [InlineData(null)]
    public void NoticesWithoutVersionTargetsStillReachEveryone(string? current) =>
        Assert.True(new CdnWarning().AppliesToVersion(current));
}
