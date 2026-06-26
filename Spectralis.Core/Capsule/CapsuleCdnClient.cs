using System.Net.Http.Headers;
using System.Text.Json;

namespace Spectralis.Core.Capsule;

public sealed class CapsuleCdnClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static readonly Uri DefaultCdnBase = new("https://cdn.deltavdevs.com/");

    private readonly HttpClient _httpClient;
    private readonly Uri _cdnBase;

    public CapsuleCdnClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }, DefaultCdnBase)
    {
    }

    public CapsuleCdnClient(HttpClient httpClient, Uri? cdnBase = null)
    {
        _httpClient = httpClient;
        _cdnBase = cdnBase ?? DefaultCdnBase;
    }

    public async Task<CreatorKeyMetadata?> FetchCreatorKeyAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var path = string.Format(CapsuleFormat.CdnKeyEndpointTemplate, fingerprint.ToLowerInvariant());
        var uri = new Uri(_cdnBase, path);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<CreatorKeyMetadata>(stream, JsonOptions, cancellationToken);
    }

    public void Dispose() => _httpClient.Dispose();
}
