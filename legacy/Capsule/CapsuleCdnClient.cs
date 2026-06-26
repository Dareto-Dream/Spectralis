using System.Net.Http.Headers;
using System.Text.Json;

namespace Spectralis;

internal sealed class CapsuleCdnClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly Uri CdnBase = new("https://cdn.deltavdevs.com/");

    private readonly HttpClient httpClient;

    public CapsuleCdnClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(20) }) { }

    internal CapsuleCdnClient(HttpClient httpClient) => this.httpClient = httpClient;

    public async Task<CreatorKeyMetadata?> FetchCreatorKeyAsync(
        string fingerprint,
        CancellationToken cancellationToken)
    {
        var path = string.Format(CapsuleFormat.CdnKeyEndpointTemplate, fingerprint.ToLowerInvariant());
        var uri = new Uri(CdnBase, path);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<CreatorKeyMetadata>(stream, JsonOptions, cancellationToken);
    }

    public void Dispose() => httpClient.Dispose();
}
