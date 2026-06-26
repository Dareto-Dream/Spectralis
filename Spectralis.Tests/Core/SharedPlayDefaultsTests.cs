using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public class SharedPlayDefaultsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://insecure.example.com")] // https required
    public void NormalizeCdnBaseUrl_FallsBackToDefault(string? input)
    {
        Assert.Equal(SharedPlayDefaults.CdnBaseUrl, SharedPlayDefaults.NormalizeCdnBaseUrl(input));
    }

    [Fact]
    public void NormalizeCdnBaseUrl_KeepsAuthorityOnly()
    {
        Assert.Equal(
            "https://cdn.example.com",
            SharedPlayDefaults.NormalizeCdnBaseUrl("https://cdn.example.com/some/path?q=1"));
    }

    [Fact]
    public void BuildWebShareJoinUrl_EncodesSessionId()
    {
        var url = SharedPlayDefaults.BuildWebShareJoinUrl(new Uri("https://cdn.example.com"), "abc 123");

        Assert.StartsWith("https://cdn.example.com/spectralis/web-share/index.html", url.ToString());
        Assert.Contains("session=abc%20123", url.Query);
    }

    [Fact]
    public void ConvertToDiscordActivityJoinUrl_AddsSourceAndMode()
    {
        var joinUrl = SharedPlayDefaults
            .BuildWebShareJoinUrl(new Uri("https://cdn.example.com"), "xyz")
            .ToString();

        var activityUrl = SharedPlayDefaults.ConvertToDiscordActivityJoinUrl(joinUrl);

        Assert.Contains("session=xyz", activityUrl);
        Assert.Contains("source=discord", activityUrl);
        Assert.Contains("mode=activity", activityUrl);
    }

    [Fact]
    public void ConvertToDiscordActivityJoinUrl_PassesThroughUnparseableInput()
    {
        Assert.Equal("not-a-url", SharedPlayDefaults.ConvertToDiscordActivityJoinUrl("not-a-url"));
        Assert.Equal(
            "https://cdn.example.com/no-session",
            SharedPlayDefaults.ConvertToDiscordActivityJoinUrl("https://cdn.example.com/no-session"));
    }
}
