using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public class SharedPlayJoinRequestTests
{
    [Fact]
    public void Parse_SpectralisUriWithSessionQuery()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("spectralis://join?session=abc-123", false, out var request));
        Assert.Equal("abc-123", request.SessionId);
        Assert.Null(request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_HttpsJoinLink_CapturesAuthorityAsCdn()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("https://share.example.com/join/xyz9", false, out var request));
        Assert.Equal("xyz9", request.SessionId);
        Assert.Equal("https://share.example.com", request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_CdnOverrideMustBeHttps()
    {
        Assert.True(SharedPlayJoinRequest.TryParse(
            "spectralis://join?session=abc&cdn=http://evil.example.com", false, out var request));
        // http:// CDN override is dropped, not honored.
        Assert.Null(request.CdnBaseUrl);

        Assert.True(SharedPlayJoinRequest.TryParse(
            "spectralis://join?session=abc&cdn=https://cdn.example.com/path", false, out request));
        Assert.Equal("https://cdn.example.com", request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_SessionIdIsCharacterAllowlisted()
    {
        Assert.True(SharedPlayJoinRequest.TryParse(
            "spectralis://join?session=ab%2F..%5Cc%20d!@#", false, out var request));
        // Everything outside [A-Za-z0-9._:-] is stripped.
        Assert.Equal("ab..cd", request.SessionId);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x")]
    [InlineData("ftp://host/join?session=abc")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_DisallowedSchemes_Rejected(string input)
    {
        Assert.False(SharedPlayJoinRequest.TryParse(input, false, out _));
    }

    [Fact]
    public void Parse_RawSessionId_OnlyWhenExplicitlyAllowed()
    {
        Assert.False(SharedPlayJoinRequest.TryParse("plain-session-id", false, out _));
        Assert.True(SharedPlayJoinRequest.TryParse("plain-session-id", true, out var request));
        Assert.Equal("plain-session-id", request.SessionId);
    }

    [Theory]
    [InlineData("spectralis://join")]
    [InlineData("spectralis://join?session=join")]
    [InlineData("spectralis://join?session=index.html")]
    public void Parse_ReservedOrMissingSessionIds_Rejected(string input)
    {
        Assert.False(SharedPlayJoinRequest.TryParse(input, false, out _));
    }
}
