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
    public void BuildWebShareJoinUrl_EncodesRoomCode()
    {
        var url = SharedPlayDefaults.BuildWebShareJoinUrl(new Uri("https://cdn.example.com"), "X7K29Q");

        Assert.Equal("https://player.deltavdevs.com/sessions/X7K29Q", url.ToString());
    }

    [Theory]
    [InlineData("https://player.deltavdevs.com/sessions/AB12CD")]
    [InlineData("https://player.deltavdevs.com/sessions/ab1-2cd")]
    [InlineData("https://player.deltavdevs.com/sessions/AB12CD?source=discord")]
    [InlineData("https://old.example.com/spectralis/web-share/?session=AB12CD")]
    public void TryReadRoomCode_UnderstandsPathAndQueryJoinLinks(string link)
    {
        Assert.Equal("AB12CD", SharedPlayDefaults.TryReadRoomCode(new Uri(link)));
    }

    [Theory]
    [InlineData("https://player.deltavdevs.com/rooms/room-abc123")]
    [InlineData("https://player.deltavdevs.com/sessions/")]
    [InlineData("https://player.deltavdevs.com/sessions/TOOSHORT1")]
    public void TryReadRoomCode_IgnoresLinksThatAreNotSessions(string link)
    {
        Assert.Null(SharedPlayDefaults.TryReadRoomCode(new Uri(link)));
    }

    [Fact]
    public void ConvertToDiscordActivityJoinUrl_AddsSourceAndMode()
    {
        var joinUrl = SharedPlayDefaults
            .BuildWebShareJoinUrl(new Uri("https://cdn.example.com"), "AB12CD")
            .ToString();

        var activityUrl = SharedPlayDefaults.ConvertToDiscordActivityJoinUrl(joinUrl);

        Assert.Equal("https://player.deltavdevs.com/sessions/AB12CD?source=discord", activityUrl);
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
