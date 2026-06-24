using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Spectralis;

internal sealed class SharedPlayCdnClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient httpClient;

    public SharedPlayCdnClient()
        : this(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(45)
        })
    {
    }

    internal SharedPlayCdnClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<SharedPlaySession> CreateSessionAndUploadAsync(
        Uri cdnBaseUri,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var response = await RequestUploadUrlsAsync(
            SharedPlayDefaults.BuildEndpoint(cdnBaseUri, "/shared-play/v1/upload-urls"),
            package,
            playback,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(response.SessionId))
            throw new InvalidOperationException("The CDN did not return a Shared Play session ID.");

        if (!TryCreateHttpsUri(response.StateUrl, out var stateUri))
            throw new InvalidOperationException("The CDN did not return a valid HTTPS Shared Play state URL.");
        var queueUri = TryCreateHttpsUri(response.QueueUrl, out var parsedQueueUri)
            ? parsedQueueUri
            : SharedPlayDefaults.BuildEndpoint(cdnBaseUri, $"/shared-play/v1/sessions/{Uri.EscapeDataString(response.SessionId)}/queue");

        var joinUri = SharedPlayDefaults.BuildWebShareJoinUrl(cdnBaseUri, response.SessionId);

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static upload => string.Equals(upload.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        return new SharedPlaySession
        {
            SessionId = response.SessionId,
            JoinUrl = joinUri.ToString(),
            TrackId = string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            StateUrl = stateUri,
            QueueUrl = queueUri,
            ExpiresAtUtc = response.ExpiresAtUtc
        };
    }

    public async Task<SharedPlaySession> UploadTrackToSessionAsync(
        Uri cdnBaseUri,
        SharedPlaySession session,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var encodedSessionId = Uri.EscapeDataString(session.SessionId);
        var response = await RequestUploadUrlsAsync(
            SharedPlayDefaults.BuildEndpoint(cdnBaseUri, $"/shared-play/v1/sessions/{encodedSessionId}/tracks"),
            package,
            playback,
            cancellationToken);

        if (!TryCreateHttpsUri(response.StateUrl, out var stateUri))
            stateUri = session.StateUrl;
        var queueUri = TryCreateHttpsUri(response.QueueUrl, out var parsedQueueUri)
            ? parsedQueueUri
            : session.QueueUrl;

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static upload => string.Equals(upload.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        return new SharedPlaySession
        {
            SessionId = string.IsNullOrWhiteSpace(response.SessionId) ? session.SessionId : response.SessionId,
            JoinUrl = session.JoinUrl,
            TrackId = string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            StateUrl = stateUri,
            QueueUrl = queueUri,
            ExpiresAtUtc = response.ExpiresAtUtc ?? session.ExpiresAtUtc
        };
    }

    public async Task<SharedPlayPreparedTrack> PrepareTrackInSessionAsync(
        Uri cdnBaseUri,
        SharedPlaySession session,
        string fileKey,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var encodedSessionId = Uri.EscapeDataString(session.SessionId);
        var response = await RequestUploadUrlsAsync(
            SharedPlayDefaults.BuildEndpoint(cdnBaseUri, $"/shared-play/v1/sessions/{encodedSessionId}/tracks"),
            package,
            playback,
            cancellationToken,
            activateOnUpload: false);

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static upload => string.Equals(upload.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        var packageUrl = TryCreateHttpsUri(uploadTarget.AssetUrl, out var parsedPackageUri)
            ? parsedPackageUri
            : SharedPlayDefaults.BuildEndpoint(
                cdnBaseUri,
                $"/shared-play/v1/packages/{encodedSessionId}/tracks/{Uri.EscapeDataString(TrackAssetKey(package.TrackId))}/spectralis-rich.zip");

        return new SharedPlayPreparedTrack(
            fileKey,
            string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            packageUrl,
            package.Track,
            DateTimeOffset.UtcNow);
    }

    public async Task<SharedPlaySession> ActivatePreparedTrackAsync(
        Uri cdnBaseUri,
        SharedPlaySession session,
        SharedPlayPreparedTrack preparedTrack,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var trackKey = TrackAssetKey(preparedTrack.TrackId);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            SharedPlayDefaults.BuildEndpoint(
                cdnBaseUri,
                $"/shared-play/v1/sessions/{Uri.EscapeDataString(session.SessionId)}/tracks/{Uri.EscapeDataString(trackKey)}/activate"))
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                sessionId = session.SessionId,
                trackId = preparedTrack.TrackId,
                activeTrackId = preparedTrack.TrackId,
                currentTrackId = preparedTrack.TrackId,
                playback = playback with { TrackId = preparedTrack.TrackId },
                state = playback with { TrackId = preparedTrack.TrackId }
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play prepared track activation", cancellationToken);

        return new SharedPlaySession
        {
            SessionId = session.SessionId,
            JoinUrl = session.JoinUrl,
            TrackId = preparedTrack.TrackId,
            StateUrl = session.StateUrl,
            QueueUrl = session.QueueUrl,
            ExpiresAtUtc = session.ExpiresAtUtc
        };
    }

    public async Task<SharedPlayRemoteSession> FetchSessionAsync(
        Uri cdnBaseUri,
        string sessionId,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var normalizedSessionId = NormalizeSessionId(sessionId);
        if (string.IsNullOrWhiteSpace(normalizedSessionId))
            throw new InvalidOperationException("Shared Play session ID was empty.");

        var encodedSessionId = Uri.EscapeDataString(normalizedSessionId);
        var candidates = new[]
        {
            $"/shared-play/v1/sessions/{encodedSessionId}",
            $"/shared-play/v1/sessions/{encodedSessionId}/manifest",
            $"/shared-play/v1/join/{encodedSessionId}",
            $"/shared-play/join/{encodedSessionId}"
        };

        Exception? lastError = null;
        foreach (var candidate in candidates)
        {
            try
            {
                return await FetchSessionFromEndpointAsync(
                    SharedPlayDefaults.BuildEndpoint(cdnBaseUri, candidate),
                    cdnBaseUri,
                    normalizedSessionId,
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException("Shared Play session could not be loaded from the CDN.", lastError);
    }

    public async Task<SharedPlayPlaybackSnapshot?> FetchPlaybackStateAsync(
        Uri stateUrl,
        CancellationToken cancellationToken)
    {
        EnsureHttps(stateUrl, "Shared Play state URL");

        using var request = new HttpRequestMessage(HttpMethod.Get, stateUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play state request", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return TryReadPlaybackSnapshot(document.RootElement, out var playback) ? playback : null;
    }

    public async Task DownloadPackageAsync(
        Uri packageUrl,
        string targetPath,
        CancellationToken cancellationToken)
    {
        EnsureHttps(packageUrl, "Shared Play package URL");

        using var response = await httpClient.GetAsync(packageUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play package download", cancellationToken);

        var contentLength = response.Content.Headers.ContentLength;
        if (contentLength is > SharedPlayDefaults.MaxPackageBytes)
            throw new InvalidOperationException("Shared Play package is larger than the download safety limit.");

        var directory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[1024 * 128];
        long totalBytes = 0;

        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;

            totalBytes += read;
            if (totalBytes > SharedPlayDefaults.MaxPackageBytes)
                throw new InvalidOperationException("Shared Play package is larger than the download safety limit.");

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task PublishPlaybackStateAsync(
        SharedPlaySession session,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(session.StateUrl, "Shared Play state URL");
        var playbackWithTrack = playback with { TrackId = session.TrackId };

        using var request = new HttpRequestMessage(HttpMethod.Post, session.StateUrl)
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                sessionId = session.SessionId,
                trackId = session.TrackId,
                activeTrackId = session.TrackId,
                currentTrackId = session.TrackId,
                state = playbackWithTrack,
                playback = playbackWithTrack
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play playback publish", cancellationToken);
    }

    public async Task<SharedPlayQueueSnapshot?> FetchQueueStateAsync(
        Uri queueUrl,
        CancellationToken cancellationToken)
    {
        EnsureHttps(queueUrl, "Shared Play queue URL");

        using var request = new HttpRequestMessage(HttpMethod.Get, queueUrl);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play queue request", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedPlayQueueSnapshot>(stream, JsonOptions, cancellationToken);
    }

    public async Task PublishQueueStateAsync(
        SharedPlaySession session,
        SharedPlayQueueSnapshot queue,
        CancellationToken cancellationToken)
    {
        EnsureHttps(session.QueueUrl, "Shared Play queue URL");

        using var request = new HttpRequestMessage(HttpMethod.Post, session.QueueUrl)
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                sessionId = session.SessionId,
                queue
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play queue publish", cancellationToken);
    }

    public async Task<SharedPlayChannelResponse?> PublishChannelAsync(
        Uri cdnBaseUri,
        string channelId,
        SharedPlayChannelPublishRequest payload,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = SharedPlayDefaults.BuildEndpoint(
            cdnBaseUri,
            $"/shared-play/v1/channels/{Uri.EscapeDataString(channelId)}");

        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = JsonContent(payload)
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play live channel publish", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedPlayChannelResponse>(stream, JsonOptions, cancellationToken);
    }

    private async Task<SharedPlayUploadResponse> RequestUploadUrlsAsync(
        Uri endpoint,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken,
        bool activateOnUpload = true)
    {
        var playbackWithTrack = playback with { TrackId = package.TrackId };
        var uploadRequest = new SharedPlayUploadRequest(
            SharedPlayDefaults.ProtocolVersion,
            SharedPlayDefaults.ClientName,
            "spectralis-rich",
            package.Track,
            new SharedPlayPackageDescriptor(
                package.TrackId,
                package.AudioSha256,
                package.PackageSha256,
                package.AudioBytes,
                package.PackageBytes,
                package.AudioExtension,
                SharedPlayDefaults.RichPackageContentType),
            playbackWithTrack,
            SharedPlayCacheStore.CreateCapabilities());

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(new
            {
                uploadRequest.ProtocolVersion,
                uploadRequest.ClientName,
                uploadRequest.PackageKind,
                uploadRequest.Track,
                uploadRequest.Package,
                uploadRequest.Playback,
                uploadRequest.Capabilities,
                activateOnUpload
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play upload session request", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedPlayUploadResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The CDN returned an empty Shared Play upload response.");
    }

    private async Task<SharedPlayRemoteSession> FetchSessionFromEndpointAsync(
        Uri endpoint,
        Uri cdnBaseUri,
        string fallbackSessionId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play session fetch", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadRemoteSession(document.RootElement, cdnBaseUri, fallbackSessionId);
    }

    private static SharedPlayRemoteSession ReadRemoteSession(
        JsonElement payload,
        Uri cdnBaseUri,
        string fallbackSessionId)
    {
        var source = TryGetObject(payload, "session", out var sessionElement)
            ? sessionElement
            : payload;

        var sessionId = FirstString(source, "sessionId") ??
            FirstString(payload, "sessionId") ??
            fallbackSessionId;

        if (string.IsNullOrWhiteSpace(sessionId))
            throw new InvalidOperationException("The CDN session response did not include a session ID.");

        var encodedSessionId = Uri.EscapeDataString(sessionId);
        var trackId = FirstString(source, "activeTrackId", "currentTrackId", "trackId") ??
            FirstString(payload, "activeTrackId", "currentTrackId", "trackId");
        var stateUrl = FirstUrl(
            cdnBaseUri,
            FirstString(source, "stateUrl"),
            FirstNestedString(source, "links", "state"),
            FirstString(payload, "stateUrl"),
            FirstNestedString(payload, "links", "state"),
            $"/shared-play/v1/sessions/{encodedSessionId}/state");

        if (stateUrl is null)
            throw new InvalidOperationException("The CDN session response did not include a valid state URL.");

        var packageUrl = FirstUrl(
            cdnBaseUri,
            FirstString(source, "packageUrl"),
            FirstString(source, "spectralisPackageUrl"),
            FirstString(source, "assetUrl"),
            FirstNestedString(source, "package", "assetUrl"),
            FirstNestedString(source, "package", "url"),
            FirstNestedString(source, "assets", "spectralisPackage", "url"),
            FirstNestedString(source, "assets", "package", "url"),
            FirstNestedString(payload, "package", "assetUrl"),
            FindUploadAssetUrl(source, "spectralis-package"),
            FindUploadAssetUrl(payload, "spectralis-package"));

        if (packageUrl is null)
            throw new InvalidOperationException("The CDN session response did not include a valid package URL.");

        var queueUrl = FirstUrl(
            cdnBaseUri,
            FirstString(source, "queueUrl"),
            FirstNestedString(source, "links", "queue"),
            FirstString(payload, "queueUrl"),
            FirstNestedString(payload, "links", "queue"),
            $"/shared-play/v1/sessions/{encodedSessionId}/queue");

        if (queueUrl is null)
            throw new InvalidOperationException("The CDN session response did not include a valid queue URL.");

        return new SharedPlayRemoteSession(
            sessionId,
            trackId,
            stateUrl,
            queueUrl,
            packageUrl,
            FirstDateTimeOffset(source, "expiresAtUtc") ?? FirstDateTimeOffset(payload, "expiresAtUtc"));
    }

    private async Task UploadPackageAsync(
        SharedPlayUploadTarget uploadTarget,
        SharedPlayPackage package,
        CancellationToken cancellationToken)
    {
        if (!TryCreateHttpsUri(uploadTarget.UploadUrl, out var uploadUri))
            throw new InvalidOperationException("The CDN returned an invalid Shared Play package upload URL.");

        var method = string.IsNullOrWhiteSpace(uploadTarget.Method)
            ? HttpMethod.Put
            : new HttpMethod(uploadTarget.Method.Trim().ToUpperInvariant());

        using var packageStream = new FileStream(package.PackagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var content = new StreamContent(packageStream);
        content.Headers.ContentLength = package.PackageBytes;
        content.Headers.ContentType = new MediaTypeHeaderValue(SharedPlayDefaults.RichPackageContentType);

        using var request = new HttpRequestMessage(method, uploadUri)
        {
            Content = content
        };

        ApplyUploadHeaders(request, uploadTarget.Headers);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play package upload", cancellationToken);
    }

    private static void ApplyUploadHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null)
            return;

        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value))
                continue;

            if (IsRestrictedHeader(name, value))
                continue;

            if (!request.Headers.TryAddWithoutValidation(name, value))
                request.Content?.Headers.TryAddWithoutValidation(name, value);
        }
    }

    private static bool IsRestrictedHeader(string name, string value)
    {
        if (string.Equals(name, "host", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "content-length", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(name, "authorization", StringComparison.OrdinalIgnoreCase) &&
            value.TrimStart().StartsWith("Basic ", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadPlaybackSnapshot(JsonElement payload, out SharedPlayPlaybackSnapshot playback)
    {
        playback = new SharedPlayPlaybackSnapshot(false, 0, 0, "remote", DateTimeOffset.UtcNow);
        var payloadTrackId = FirstString(payload, "activeTrackId", "currentTrackId", "trackId");

        if (TryGetObject(payload, "playback", out var playbackElement) &&
            HasPlaybackFields(playbackElement))
        {
            playback = ReadPlaybackSnapshot(playbackElement, payloadTrackId);
            return true;
        }

        if (TryGetObject(payload, "state", out var stateElement))
        {
            if (TryGetObject(stateElement, "playback", out var statePlaybackElement) &&
                HasPlaybackFields(statePlaybackElement))
            {
                playback = ReadPlaybackSnapshot(statePlaybackElement, payloadTrackId);
                return true;
            }

            if (HasPlaybackFields(stateElement))
            {
                playback = ReadPlaybackSnapshot(stateElement, payloadTrackId);
                return true;
            }
        }

        if (TryGetObject(payload, "session", out var sessionElement) &&
            TryGetObject(sessionElement, "playback", out var sessionPlaybackElement) &&
            HasPlaybackFields(sessionPlaybackElement))
        {
            playback = ReadPlaybackSnapshot(sessionPlaybackElement, payloadTrackId);
            return true;
        }

        if (HasPlaybackFields(payload))
        {
            playback = ReadPlaybackSnapshot(payload, payloadTrackId);
            return true;
        }

        return false;
    }

    private static SharedPlayPlaybackSnapshot ReadPlaybackSnapshot(JsonElement element, string? fallbackTrackId) =>
        new(
            FirstBool(element, "isPlaying") ?? false,
            Math.Max(0, FirstDouble(element, "positionSeconds", "position") ?? 0),
            Math.Max(0, FirstDouble(element, "durationSeconds", "duration") ?? 0),
            FirstString(element, "reason") ?? "remote",
            FirstDateTimeOffset(element, "hostClockUtc", "updatedAtUtc", "createdAtUtc") ?? DateTimeOffset.UtcNow,
            fallbackTrackId ?? FirstString(element, "activeTrackId", "currentTrackId", "trackId"));

    private static bool HasPlaybackFields(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object &&
        (element.TryGetProperty("isPlaying", out _) ||
            element.TryGetProperty("positionSeconds", out _) ||
            element.TryGetProperty("position", out _) ||
            element.TryGetProperty("hostClockUtc", out _));

    private static Uri? FirstUrl(Uri cdnBaseUri, params string?[] values)
    {
        foreach (var value in values)
        {
            if (TryCreateHttpsUri(cdnBaseUri, value, out var uri))
                return uri;
        }

        return null;
    }

    private static bool TryCreateHttpsUri(Uri cdnBaseUri, string? value, out Uri uri)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            if (Uri.TryCreate(value.Trim(), UriKind.Absolute, out var absolute) &&
                string.Equals(absolute.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                uri = absolute;
                return true;
            }

            if (Uri.TryCreate(cdnBaseUri, value.TrimStart('/'), out var relative) &&
                string.Equals(relative.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                uri = relative;
                return true;
            }
        }

        uri = new Uri(SharedPlayDefaults.CdnBaseUrl);
        return false;
    }

    private static string? FindUploadAssetUrl(JsonElement element, string name)
    {
        if (!TryGetArray(element, "uploads", out var uploads))
            return null;

        foreach (var upload in uploads.EnumerateArray())
        {
            if (upload.ValueKind != JsonValueKind.Object)
                continue;

            var uploadName = FirstString(upload, "name");
            if (string.Equals(uploadName, name, StringComparison.OrdinalIgnoreCase))
                return FirstString(upload, "assetUrl");
        }

        return null;
    }

    private static string? FirstNestedString(JsonElement element, params string[] propertyPath)
    {
        var current = element;
        foreach (var propertyName in propertyPath)
        {
            if (!TryGetObjectProperty(current, propertyName, out current))
                return null;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

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

    private static double? FirstDouble(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetObjectProperty(element, propertyName, out var value))
                continue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
                return number;

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static bool? FirstBool(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetObjectProperty(element, propertyName, out var value))
                continue;

            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return value.GetBoolean();

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static DateTimeOffset? FirstDateTimeOffset(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            var value = FirstString(element, propertyName);
            if (DateTimeOffset.TryParse(value, out var parsed))
                return parsed;
        }

        return null;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement value) =>
        TryGetObjectProperty(element, propertyName, out value) &&
        value.ValueKind == JsonValueKind.Object;

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement value) =>
        TryGetObjectProperty(element, propertyName, out value) &&
        value.ValueKind == JsonValueKind.Array;

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string NormalizeSessionId(string value)
    {
        var chars = value
            .Trim()
            .Where(static character =>
                char.IsAsciiLetterOrDigit(character) ||
                character is '.' or '_' or ':' or '-')
            .ToArray();

        return new string(chars);
    }

    private static string TrackAssetKey(string trackId)
    {
        var chars = trackId
            .Trim()
            .Select(static character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? Guid.NewGuid().ToString("N") : normalized[..Math.Min(96, normalized.Length)];
    }

    private static HttpContent JsonContent<T>(T value) =>
        new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = "";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
                detail = $" Response: {body.Trim()}";
        }
        catch
        {
            // The status code is enough for the user-facing diagnostic.
        }

        throw new HttpRequestException(
            $"{operation} failed with HTTP {(int)response.StatusCode} {response.ReasonPhrase}.{detail}",
            null,
            response.StatusCode);
    }

    private static bool TryCreateHttpsUri(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            uri = parsed;
            return true;
        }

        uri = new Uri(SharedPlayDefaults.CdnBaseUrl);
        return false;
    }

    private static void EnsureHttps(Uri uri, string label)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{label} must use HTTPS.");
    }

    public void Dispose()
    {
        httpClient.Dispose();
    }
}
