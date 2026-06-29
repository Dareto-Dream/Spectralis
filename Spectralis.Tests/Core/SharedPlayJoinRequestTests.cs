using Spectralis.Core.SharedPlay;
using Xunit;

namespace Spectralis.Tests.Core;

public class SharedPlayJoinRequestTests
{
    [Fact]
    public void Parse_SpectralisUriWithSessionQuery_ValidRoomCode()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("spectralis://join?session=X7K29Q", false, out var request));
        Assert.Equal("X7K29Q", request.RoomCode);
        Assert.Null(request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_SpectralisUri_DashFormattedCode_StrippedAndNormalized()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("spectralis://join?session=X7K-29Q", false, out var request));
        Assert.Equal("X7K29Q", request.RoomCode);
        Assert.Equal("X7K-29Q", request.DisplayCode);
    }

    [Fact]
    public void Parse_HttpsJoinLink_CapturesAuthorityAsCdn()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("https://share.example.com/join/AB12CD", false, out var request));
        Assert.Equal("AB12CD", request.RoomCode);
        Assert.Equal("https://share.example.com", request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_HttpsJoinLink_WithSessionQueryParam()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("https://cdn.example.com/spectralis/web-share?session=ZZ9ZZZ", false, out var request));
        Assert.Equal("ZZ9ZZZ", request.RoomCode);
        Assert.Equal("https://cdn.example.com", request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_CdnOverrideMustBeHttps()
    {
        Assert.True(SharedPlayJoinRequest.TryParse(
            "spectralis://join?session=ABC123&cdn=http://evil.example.com", false, out var request));
        Assert.Null(request.CdnBaseUrl);

        Assert.True(SharedPlayJoinRequest.TryParse(
            "spectralis://join?session=ABC123&cdn=https://cdn.example.com/path", false, out request));
        Assert.Equal("https://cdn.example.com", request.CdnBaseUrl);
    }

    [Fact]
    public void Parse_RawCode_OnlyWhenExplicitlyAllowed()
    {
        Assert.False(SharedPlayJoinRequest.TryParse("X7K29Q", false, out _));
        Assert.True(SharedPlayJoinRequest.TryParse("X7K29Q", true, out var request));
        Assert.Equal("X7K29Q", request.RoomCode);
    }

    [Fact]
    public void Parse_RawCode_WithDash_NormalizedWhenAllowed()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("X7K-29Q", true, out var request));
        Assert.Equal("X7K29Q", request.RoomCode);
    }

    [Fact]
    public void Parse_RawCode_Lowercase_UppercasedWhenAllowed()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("x7k29q", true, out var request));
        Assert.Equal("X7K29Q", request.RoomCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///C:/x")]
    [InlineData("ftp://host/join?session=ABC123")]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_DisallowedSchemes_Rejected(string input)
    {
        Assert.False(SharedPlayJoinRequest.TryParse(input, false, out _));
    }

    [Theory]
    [InlineData("ABCDE")]
    [InlineData("ABCDEFG")]
    [InlineData("TOOSH")]
    [InlineData("1234567")]
    public void Parse_WrongLengthCodes_Rejected(string input)
    {
        Assert.False(SharedPlayJoinRequest.TryParse(input, true, out _));
    }

    [Fact]
    public void Parse_EmptySessionParam_Rejected()
    {
        Assert.False(SharedPlayJoinRequest.TryParse("spectralis://join?session=", false, out _));
        Assert.False(SharedPlayJoinRequest.TryParse("spectralis://join", false, out _));
    }

    [Fact]
    public void Parse_CodeQueryParam_AcceptedAsFallback()
    {
        Assert.True(SharedPlayJoinRequest.TryParse("https://cdn.example.com/web-share?code=ABC123", false, out var request));
        Assert.Equal("ABC123", request.RoomCode);
    }
}
