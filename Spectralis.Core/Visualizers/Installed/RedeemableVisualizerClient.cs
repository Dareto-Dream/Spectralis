using System.Net.Http.Headers;
using System.Text.Json;

namespace Spectralis.Core.Visualizers.Installed;

public sealed class RedeemableVisualizerClient : IDisposable
{
    public const string DefaultManifestUrl = "https://cdn.deltavdevs.com/spectralis/visualizers";

    private const int MaxManifestBytes = 2 * 1024 * 1024;
    private const int MaxModuleBytes = 256 * 1024;
    private const int MaxBinaryBytes = 8 * 1024 * 1024;
    private const int MaxDataBlockBytes = 512 * 1024;
    private const int MaxAssetBytes = 16 * 1024 * 1024;

    private readonly HttpClient _httpClient;

    public RedeemableVisualizerClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) })
    {
    }

    internal RedeemableVisualizerClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<RedeemableVisualizerPackage> RedeemAsync(
        string redeemKey,
        CancellationToken cancellationToken)
    {
        var normalizedKey = NormalizeRedeemKey(redeemKey);
        if (string.IsNullOrWhiteSpace(normalizedKey))
            throw new InvalidOperationException("Enter a visualizer redeem key first.");

        Exception? lastError = null;
        foreach (var manifestUri in GetManifestCandidates(new Uri(DefaultManifestUrl)))
        {
            try
            {
                var manifest = await DownloadJsonAsync(manifestUri, MaxManifestBytes, cancellationToken);
                var entry = ManifestEntry.Find(manifest, normalizedKey);
                if (entry is null)
                    continue;

                return await DownloadPackageAsync(entry, manifestUri, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            "That key could not be redeemed from the Spectralis visualizer manifest.",
            lastError);
    }

    private async Task<RedeemableVisualizerPackage> DownloadPackageAsync(
        ManifestEntry entry,
        Uri manifestUri,
        CancellationToken cancellationToken)
    {
        var manifestAssetBaseUri = GetAssetBaseUri(manifestUri);
        var packageUri = entry.PackageUrl is null
            ? null
            : ResolveHttpsUri(manifestAssetBaseUri, entry.PackageUrl, "package URL");

        var packageRoot = packageUri is null
            ? entry.Payload
            : await DownloadJsonAsync(packageUri, MaxManifestBytes, cancellationToken);

        var packageBaseUri = packageUri is null ? manifestAssetBaseUri : GetAssetBaseUri(packageUri);

        var id = FirstString(packageRoot, "id") ?? entry.Id;
        var displayName =
            FirstString(packageRoot, "name", "displayName", "title") ??
            entry.DisplayName ??
            CreateDisplayName(id);

        var moduleJson = await ResolveModuleJsonAsync(packageRoot, packageBaseUri, cancellationToken);
        var binary = await ResolveBinaryAsync(packageRoot, packageBaseUri, cancellationToken);
        var dataBlocks = await ResolveDataBlocksAsync(packageRoot, packageBaseUri, cancellationToken);
        var binaryAssets = await ResolveBinaryAssetsAsync(packageRoot, packageBaseUri, cancellationToken);

        return new RedeemableVisualizerPackage(
            id,
            displayName,
            FirstString(packageRoot, "version") ?? entry.Version,
            moduleJson,
            binary,
            dataBlocks,
            binaryAssets,
            packageBaseUri.ToString());
    }

    private async Task<string> ResolveModuleJsonAsync(JsonElement payload, Uri baseUri, CancellationToken ct)
    {
        if (TryGetObjectProperty(payload, "module", out var moduleElement))
            return moduleElement.GetRawText();

        if (FirstString(payload, "moduleJson") is { } moduleJson)
            return moduleJson;

        var moduleUrl = FirstString(payload, "moduleUrl", "moduleHref")
            ?? throw new InvalidOperationException("The visualizer manifest entry is missing moduleUrl.");

        return await DownloadTextAsync(ResolveHttpsUri(baseUri, moduleUrl, "module URL"), MaxModuleBytes, ct);
    }

    private async Task<byte[]> ResolveBinaryAsync(JsonElement payload, Uri baseUri, CancellationToken ct)
    {
        if (FirstString(payload, "binaryBase64", "wasmBase64") is { } binaryBase64)
            return Convert.FromBase64String(binaryBase64);

        var binaryUrl = FirstString(payload, "binaryUrl", "wasmUrl", "visualizerUrl")
            ?? throw new InvalidOperationException("The visualizer manifest entry is missing binaryUrl.");

        return await DownloadBytesAsync(ResolveHttpsUri(baseUri, binaryUrl, "binary URL"), MaxBinaryBytes, ct);
    }

    private async Task<IReadOnlyDictionary<string, string>> ResolveDataBlocksAsync(
        JsonElement payload, Uri baseUri, CancellationToken ct)
    {
        var dataBlocks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetObject(payload, "data", out var dataElement))
        {
            foreach (var property in dataElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String)
                {
                    dataBlocks[property.Name] = property.Value.GetString() ?? "";
                }
                else if (TryGetObjectProperty(property.Value, "url", out var dataUrlElement) &&
                    dataUrlElement.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(dataUrlElement.GetString()))
                {
                    dataBlocks[property.Name] = await DownloadTextAsync(
                        ResolveHttpsUri(baseUri, dataUrlElement.GetString()!, "data URL"),
                        MaxDataBlockBytes, ct);
                }
                else
                {
                    dataBlocks[property.Name] = property.Value.GetRawText();
                }
            }
        }

        if (TryGetObject(payload, "dataUrls", out var dataUrlsElement))
        {
            foreach (var property in dataUrlsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(property.Value.GetString()))
                    continue;

                dataBlocks[property.Name] = await DownloadTextAsync(
                    ResolveHttpsUri(baseUri, property.Value.GetString()!, "data URL"),
                    MaxDataBlockBytes, ct);
            }
        }

        return dataBlocks;
    }

    private async Task<IReadOnlyDictionary<string, byte[]>> ResolveBinaryAssetsAsync(
        JsonElement payload, Uri baseUri, CancellationToken ct)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var propertyName in new[] { "assetUrls", "binaryAssetUrls", "assets" })
        {
            if (!TryGetObject(payload, propertyName, out var assetUrlsElement))
                continue;

            foreach (var property in assetUrlsElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(property.Value.GetString()))
                    continue;

                assets[property.Name] = await DownloadBytesAsync(
                    ResolveHttpsUri(baseUri, property.Value.GetString()!, "asset URL"),
                    MaxAssetBytes, ct);
            }
        }

        return assets;
    }

    private async Task<JsonElement> DownloadJsonAsync(Uri uri, int maxBytes, CancellationToken ct)
    {
        var text = await DownloadTextAsync(uri, maxBytes, ct);
        using var document = JsonDocument.Parse(text, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        return document.RootElement.Clone();
    }

    private async Task<string> DownloadTextAsync(Uri uri, int maxBytes, CancellationToken ct)
    {
        var bytes = await DownloadBytesAsync(uri, maxBytes, ct);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    private async Task<byte[]> DownloadBytesAsync(Uri uri, int maxBytes, CancellationToken ct)
    {
        EnsureHttps(uri);

        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maxBytes)
            throw new InvalidOperationException($"The visualizer asset at {uri} is larger than the safety limit.");

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[32 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (memory.Length + read > maxBytes)
                throw new InvalidOperationException($"The visualizer asset at {uri} is larger than the safety limit.");
            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    private static Uri ResolveHttpsUri(Uri baseUri, string value, string label)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var absolute))
            absolute = new Uri(baseUri, value.Trim());

        if (!string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"The visualizer {label} must use HTTPS.");

        return absolute;
    }

    private static Uri GetAssetBaseUri(Uri uri)
    {
        var text = uri.ToString();
        if (text.EndsWith("/", StringComparison.Ordinal))
            return uri;

        var fileName = Path.GetFileName(uri.AbsolutePath);
        return fileName.Contains('.', StringComparison.Ordinal)
            ? uri
            : new Uri($"{text}/");
    }

    private static IEnumerable<Uri> GetManifestCandidates(Uri manifestUri)
    {
        EnsureHttps(manifestUri);
        yield return manifestUri;
        var withoutSlash = manifestUri.ToString().TrimEnd('/');
        if (!withoutSlash.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            yield return new Uri($"{withoutSlash}.json");
            yield return new Uri($"{withoutSlash}/manifest.json");
            yield return new Uri($"{withoutSlash}/index.json");
        }
    }

    private static string NormalizeRedeemKey(string value) =>
        new string(value.Trim()
            .Where(static c => char.IsAsciiLetterOrDigit(c))
            .Select(static c => char.ToLowerInvariant(c))
            .ToArray());

    private static bool KeyMatches(string manifestKey, string redeemKey) =>
        string.Equals(NormalizeRedeemKey(manifestKey), redeemKey, StringComparison.Ordinal);

    private static string? FirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetObjectProperty(element, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                    return text.Trim();
            }
        }
        return null;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value) =>
        TryGetObjectProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement value) =>
        TryGetObjectProperty(element, propertyName, out value) && value.ValueKind == JsonValueKind.Array;

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
            return true;
        value = default;
        return false;
    }

    private static string CreateDisplayName(string value) =>
        string.Join(' ',
            value.Replace('_', ' ').Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(static part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static void EnsureHttps(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Redeemable visualizer URLs must use HTTPS.");
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private sealed record ManifestEntry(
        string Id,
        string? DisplayName,
        string? Version,
        string? PackageUrl,
        JsonElement Payload)
    {
        public static ManifestEntry? Find(JsonElement manifest, string normalizedRedeemKey)
        {
            var redeemMap = ReadRedeemMap(manifest);
            var visualizers = ReadVisualizers(manifest);

            foreach (var item in visualizers)
            {
                var id = FirstString(item, "id", "slug");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (EntryContainsKey(item, normalizedRedeemKey) ||
                    (redeemMap.TryGetValue(normalizedRedeemKey, out var mappedId) &&
                        string.Equals(mappedId, id, StringComparison.OrdinalIgnoreCase)))
                {
                    return new ManifestEntry(
                        id.Trim(),
                        FirstString(item, "name", "displayName", "title"),
                        FirstString(item, "version"),
                        FirstString(item, "packageUrl", "url"),
                        item.Clone());
                }
            }

            return null;
        }

        private static IReadOnlyList<JsonElement> ReadVisualizers(JsonElement manifest)
        {
            if (manifest.ValueKind == JsonValueKind.Array)
                return manifest.EnumerateArray().Select(static item => item.Clone()).ToArray();

            foreach (var propertyName in new[] { "visualizers", "items", "entries" })
            {
                if (TryGetArray(manifest, propertyName, out var array))
                    return array.EnumerateArray().Select(static item => item.Clone()).ToArray();
            }

            return [];
        }

        private static Dictionary<string, string> ReadRedeemMap(JsonElement manifest)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var propertyName in new[] { "redeemKeys", "keys" })
            {
                if (!TryGetObject(manifest, propertyName, out var keys))
                    continue;

                foreach (var property in keys.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.String) continue;
                    var key = NormalizeRedeemKey(property.Name);
                    var id = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(id))
                        map[key] = id.Trim();
                }
            }

            return map;
        }

        private static bool EntryContainsKey(JsonElement entry, string normalizedRedeemKey)
        {
            foreach (var propertyName in new[] { "redeemKey", "key" })
            {
                if (FirstString(entry, propertyName) is { } key && KeyMatches(key, normalizedRedeemKey))
                    return true;
            }

            foreach (var propertyName in new[] { "redeemKeys", "keys" })
            {
                if (!TryGetArray(entry, propertyName, out var keys)) continue;
                foreach (var key in keys.EnumerateArray())
                {
                    if (key.ValueKind == JsonValueKind.String &&
                        KeyMatches(key.GetString() ?? "", normalizedRedeemKey))
                        return true;
                }
            }

            return false;
        }
    }
}
