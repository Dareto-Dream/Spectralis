using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.Core;

public class ExternalOpenIpcTests
{
    [Fact]
    public void TryParseProtocolArgument_SharedPlayJoinLink_ReturnsSharedPlayKind()
    {
        // Exactly what web-share/player.js's createSpectralisJoinUrl() builds.
        var request = ExternalOpenIpc.TryParseProtocolArgument(
            "spectralis://shared-play/join?session=X7K29Q&cdn=https%3A%2F%2Faudioplayer-production-5b83.up.railway.app");

        Assert.NotNull(request);
        Assert.Equal(ExternalOpenKind.SharedPlay, request!.Kind);
        Assert.Equal("X7K29Q", request.Value);
        Assert.Equal("https://audioplayer-production-5b83.up.railway.app", request.CdnBaseUrl);
    }

    [Fact]
    public void TryParseProtocolArgument_SharedPlayJoinLink_DashedCode_Normalized()
    {
        var request = ExternalOpenIpc.TryParseProtocolArgument("spectralis://shared-play/join?session=X7K-29Q");

        Assert.NotNull(request);
        Assert.Equal(ExternalOpenKind.SharedPlay, request!.Kind);
        Assert.Equal("X7K29Q", request.Value);
    }

    [Fact]
    public void TryParseProtocolArgument_SharedPlayJoinLink_MissingSession_ReturnsNull()
    {
        Assert.Null(ExternalOpenIpc.TryParseProtocolArgument("spectralis://shared-play/join"));
    }

    [Fact]
    public void TryParseProtocolArgument_OpenUrl_StillWorks()
    {
        var request = ExternalOpenIpc.TryParseProtocolArgument("spectralis://open?url=https://example.com/track.mp3");

        Assert.NotNull(request);
        Assert.Equal(ExternalOpenKind.Url, request!.Kind);
        Assert.Equal("https://example.com/track.mp3", request.Value);
    }

    [Fact]
    public void TryParseProtocolArgument_NonSpectralisScheme_ReturnsNull()
    {
        Assert.Null(ExternalOpenIpc.TryParseProtocolArgument("https://example.com/shared-play/join?session=X7K29Q"));
    }

    [Fact]
    public void TryParseProtocolArgument_Garbage_ReturnsNull()
    {
        Assert.Null(ExternalOpenIpc.TryParseProtocolArgument("not a url"));
        Assert.Null(ExternalOpenIpc.TryParseProtocolArgument(null));
    }
}
