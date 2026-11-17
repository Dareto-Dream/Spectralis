using System.Net;
using System.Text.Json;
using Spectralis.Core.StreamerQueue;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class StreamerQueueClientTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Opening_requests_enables_the_room_but_closing_keeps_its_public_page(bool accepting)
    {
        using var http = new HttpClient(new SettingsHandler(accepting));
        using var client = new StreamerQueueClient(http);
        var room = await client.SetAcceptingSubmissionsAsync(new Uri("https://staging.example/"), "room", "owner", accepting, default);
        Assert.Equal(accepting, room.AcceptingSubmissions);
        Assert.True(room.Enabled);
    }

    private sealed class SettingsHandler(bool accepting) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/streamer-queue/v1/rooms/room/settings", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("owner", body.RootElement.GetProperty("ownerToken").GetString());
            Assert.Equal(accepting, body.RootElement.GetProperty("acceptingSubmissions").GetBoolean());
            if (accepting) Assert.True(body.RootElement.GetProperty("enabled").GetBoolean());
            else Assert.False(body.RootElement.TryGetProperty("enabled", out _));
            Assert.False(body.RootElement.TryGetProperty("settings", out _));
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
            {
                roomId = "room", enabled = true, acceptingSubmissions = accepting,
                submissions = Array.Empty<object>(), orderedQueue = Array.Empty<object>()
            })) };
        }
    }
}
