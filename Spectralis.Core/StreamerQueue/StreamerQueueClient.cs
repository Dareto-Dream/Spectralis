using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis.Core.StreamerQueue;

public sealed class StreamerQueueClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly HttpClient http;

    public StreamerQueueClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }) { }

    internal StreamerQueueClient(HttpClient http) => this.http = http;

    public void Dispose() => http.Dispose();

    // ── Room lifecycle ────────────────────────────────────────────────────────

    public async Task<SqCreateRoomResponse> CreateRoomAsync(Uri baseUri, CancellationToken ct)
    {
        var res = await http.PostAsync(RoomListUri(baseUri), null, ct);
        return await DeserializeAsync<SqCreateRoomResponse>(res, ct);
    }

    public async Task<SqRoom> GetRoomAsync(Uri baseUri, string roomId, string? ownerToken, CancellationToken ct)
    {
        var uri = new Uri(RoomUri(baseUri, roomId) + (ownerToken is not null ? $"?ownerToken={Uri.EscapeDataString(ownerToken)}" : ""));
        var res = await http.GetAsync(uri, ct);
        return await DeserializeAsync<SqRoom>(res, ct);
    }

    public async Task<SqRoom> PutSettingsAsync(Uri baseUri, string roomId, string ownerToken, bool enabled, SqSettings settings, string? channelId, CancellationToken ct)
    {
        var body = new { ownerToken, enabled, settings, channelId };
        var res = await http.PutAsync(new Uri(RoomUri(baseUri, roomId) + "/settings"), JsonContent(body), ct);
        return await DeserializeAsync<SqRoom>(res, ct);
    }

    // ── Submissions ───────────────────────────────────────────────────────────

    public async Task<SqSubmitResponse> SubmitAsync(Uri baseUri, string roomId, SqSubmitRequest req, CancellationToken ct)
    {
        var res = await http.PostAsync(new Uri(RoomUri(baseUri, roomId) + "/submit"), JsonContent(req), ct);
        return await DeserializeAsync<SqSubmitResponse>(res, ct);
    }

    public async Task<SqSubmitResponse> UploadAsync(Uri baseUri, string roomId, Stream fileStream, string fileName, SqUploadMeta meta, CancellationToken ct)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StreamContent(fileStream), "file", fileName);
        form.Add(new StringContent(meta.DisplayName), "displayName");
        if (meta.Title is not null) form.Add(new StringContent(meta.Title), "title");
        if (meta.Artist is not null) form.Add(new StringContent(meta.Artist), "artist");
        if (meta.DurationSeconds.HasValue) form.Add(new StringContent(meta.DurationSeconds.Value.ToString()), "durationSeconds");
        form.Add(new StringContent(meta.Tier), "tier");
        if (meta.FpCookie is not null) form.Add(new StringContent(meta.FpCookie), "fpCookie");
        if (meta.FpUa is not null) form.Add(new StringContent(meta.FpUa), "fpUa");
        if (meta.FpScreen is not null) form.Add(new StringContent(meta.FpScreen), "fpScreen");
        form.Add(new StringContent(meta.FpTz.ToString()), "fpTz");
        var res = await http.PostAsync(new Uri(RoomUri(baseUri, roomId) + "/upload"), form, ct);
        return await DeserializeAsync<SqSubmitResponse>(res, ct);
    }

    public async Task<SqPromoteResponse> PromoteAsync(Uri baseUri, string roomId, string submissionId, string tier, SqFingerprintPayload fp, CancellationToken ct)
    {
        var body = new { tier, fp.FpCookie, fp.FpUa, fp.FpScreen, fp.FpTz, fp.DisplayName };
        var res = await http.PostAsync(new Uri(SubmissionUri(baseUri, roomId, submissionId) + "/promote"), JsonContent(body), ct);
        return await DeserializeAsync<SqPromoteResponse>(res, ct);
    }

    public async Task<bool> EditSubmissionAsync(Uri baseUri, string roomId, string submissionId, string? title, string? artist, SqFingerprintPayload fp, CancellationToken ct)
    {
        var body = new { title, artist, fp.FpCookie, fp.FpUa, fp.FpScreen, fp.FpTz, fp.DisplayName };
        var req = new HttpRequestMessage(new HttpMethod("PATCH"), SubmissionUri(baseUri, roomId, submissionId))
        {
            Content = JsonContent(body)
        };
        var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> ApproveAsync(Uri baseUri, string roomId, string submissionId, string ownerToken, CancellationToken ct)
    {
        var res = await http.PostAsync(new Uri(SubmissionUri(baseUri, roomId, submissionId) + "/approve"), JsonContent(new { ownerToken }), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> RejectAsync(Uri baseUri, string roomId, string submissionId, string ownerToken, CancellationToken ct)
    {
        var res = await http.PostAsync(new Uri(SubmissionUri(baseUri, roomId, submissionId) + "/reject"), JsonContent(new { ownerToken }), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> DeleteSubmissionAsync(Uri baseUri, string roomId, string submissionId, string ownerToken, CancellationToken ct)
    {
        var req = new HttpRequestMessage(HttpMethod.Delete, $"{SubmissionUri(baseUri, roomId, submissionId)}?ownerToken={Uri.EscapeDataString(ownerToken)}");
        var res = await http.SendAsync(req, ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> SetOrderAsync(Uri baseUri, string roomId, string ownerToken, IEnumerable<string> orderedIds, CancellationToken ct)
    {
        var body = new { ownerToken, order = orderedIds };
        var res = await http.PutAsync(new Uri(RoomUri(baseUri, roomId) + "/order"), JsonContent(body), ct);
        return res.IsSuccessStatusCode;
    }

    public async Task<bool> SetNowPlayingAsync(Uri baseUri, string roomId, string ownerToken, string? submissionId, CancellationToken ct)
    {
        var body = new { ownerToken, submissionId };
        var res = await http.PostAsync(new Uri(RoomUri(baseUri, roomId) + "/now-playing"), JsonContent(body), ct);
        return res.IsSuccessStatusCode;
    }

    // ── Stripe ────────────────────────────────────────────────────────────────

    public async Task<SqStripeConnectResponse> GetStripeConnectUrlAsync(Uri baseUri, string roomId, string ownerToken, CancellationToken ct)
    {
        var res = await http.GetAsync(new Uri(RoomUri(baseUri, roomId) + $"/stripe/connect?ownerToken={Uri.EscapeDataString(ownerToken)}"), ct);
        return await DeserializeAsync<SqStripeConnectResponse>(res, ct);
    }

    public async Task<bool> StripeDisconnectAsync(Uri baseUri, string roomId, string ownerToken, CancellationToken ct)
    {
        var res = await http.PostAsync(new Uri(RoomUri(baseUri, roomId) + "/stripe/disconnect"), JsonContent(new { ownerToken }), ct);
        return res.IsSuccessStatusCode;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string RoomListUri(Uri baseUri) => baseUri.AbsoluteUri.TrimEnd('/') + "/streamer-queue/v1/rooms";
    private static string RoomUri(Uri baseUri, string roomId) => RoomListUri(baseUri) + "/" + Uri.EscapeDataString(roomId);
    private static string SubmissionUri(Uri baseUri, string roomId, string subId) => RoomUri(baseUri, roomId) + "/submissions/" + Uri.EscapeDataString(subId);

    private static HttpContent JsonContent<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, JsonOpts);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static async Task<T> DeserializeAsync<T>(HttpResponseMessage res, CancellationToken ct)
    {
        if (!res.IsSuccessStatusCode)
        {
            var body = await res.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"SQ API error {(int)res.StatusCode}: {body}", null, res.StatusCode);
        }
        var stream = await res.Content.ReadAsStreamAsync(ct);
        return JsonSerializer.Deserialize<T>(stream, JsonOpts)
            ?? throw new InvalidOperationException("Null response from SQ API.");
    }
}

public sealed record SqSubmitRequest(
    string Url,
    string DisplayName,
    string? Title,
    string? Artist,
    double? DurationSeconds,
    string Tier,
    string? FpCookie,
    string? FpUa,
    string? FpScreen,
    int FpTz);

public sealed record SqUploadMeta(
    string DisplayName,
    string? Title,
    string? Artist,
    double? DurationSeconds,
    string Tier,
    string? FpCookie,
    string? FpUa,
    string? FpScreen,
    int FpTz);

public sealed record SqFingerprintPayload(
    string DisplayName,
    string? FpCookie,
    string? FpUa,
    string? FpScreen,
    int FpTz);
