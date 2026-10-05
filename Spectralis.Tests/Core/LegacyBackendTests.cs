using System.Net;
using Spectralis.App.Services;
using Spectralis.Core.SharedPlay;
using Spectralis.Core.StreamerQueue;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class LegacyBackendTests
{
    private const string Legacy = "https://audioplayer-production-5b83.up.railway.app";

    [Theory]
    [InlineData(Legacy, true)]
    [InlineData(Legacy + "/", true)]
    [InlineData("HTTPS://AudioPlayer-Production-5B83.up.railway.app/streamer-queue", true)]
    [InlineData("https://spectralis-api.deltavdevs.com", false)]
    [InlineData("https://audioplayer-staging.up.railway.app", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void RecognisesTheLegacyBackendAddress(string? url, bool expected) =>
        Assert.Equal(expected, SharedPlayDefaults.IsLegacyBackend(url));

    [Fact]
    public void LegacyAddressNormalizesToTheDefaultBackend() =>
        Assert.Equal(SharedPlayDefaults.CdnBaseUrl, SharedPlayDefaults.NormalizeCdnBaseUrl(Legacy + "/"));

    [Fact]
    public void JoinLinkFromAnOlderVersionIsPointedAtTheDefaultBackend()
    {
        var ok = SharedPlayJoinRequest.TryParse($"spectralis://shared-play/join?session=X7K29Q&cdn={Uri.EscapeDataString(Legacy)}", allowRawCode: false, out var request);

        Assert.True(ok);
        Assert.Equal(SharedPlayDefaults.CdnBaseUrl, request.CdnBaseUrl);
    }

    [Fact]
    public void SavedLegacyAddressesAreDroppedWhenSettingsLoad()
    {
        var settings = AppSettingsStore.Normalize(new AppSettings
        {
            SharedPlayCdnBaseUrl = Legacy,
            SqCdnBaseUrl = Legacy + "/",
        });

        Assert.Equal(string.Empty, settings.SharedPlayCdnBaseUrl);
        Assert.Equal(string.Empty, settings.SqCdnBaseUrl);
    }

    [Fact]
    public void OtherSavedBackendsSurviveSettingsLoad()
    {
        var settings = AppSettingsStore.Normalize(new AppSettings
        {
            SharedPlayCdnBaseUrl = SharedPlayDefaults.StagingCdnBaseUrl,
            SqCdnBaseUrl = "https://example.test/",
        });

        Assert.Equal(SharedPlayDefaults.StagingCdnBaseUrl, settings.SharedPlayCdnBaseUrl);
        Assert.Equal("https://example.test/", settings.SqCdnBaseUrl);
    }

    private sealed class Fake(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task PollingARoomThatNoLongerExistsFlagsItAsMissing()
    {
        using var controller = new StreamerQueueRoomController(new StreamerQueueClient(new HttpClient(new Fake(HttpStatusCode.NotFound, "{\"error\":\"Streamer queue room not found.\"}"))));
        controller.Configure(new Uri("https://spectralis-api.deltavdevs.com/"), "gone", "owner");

        await controller.PollAsync(CancellationToken.None);

        Assert.True(controller.RoomMissing);
    }

    [Fact]
    public async Task AServerErrorIsNotMistakenForAMissingRoom()
    {
        using var controller = new StreamerQueueRoomController(new StreamerQueueClient(new HttpClient(new Fake(HttpStatusCode.BadGateway, "bad gateway"))));
        controller.Configure(new Uri("https://spectralis-api.deltavdevs.com/"), "room", "owner");

        await controller.PollAsync(CancellationToken.None);

        Assert.False(controller.RoomMissing);
        Assert.Equal(1, controller.ConsecutiveFailureCount);
    }
}
