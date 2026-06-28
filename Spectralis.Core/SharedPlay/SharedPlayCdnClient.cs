using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.SharedPlay;

public sealed class SharedPlayCdnClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly HttpClient httpClient;

    public SharedPlayCdnClient()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(45) })
    {
    }

    internal SharedPlayCdnClient(HttpClient httpClient)
    {
        this.httpClient = httpClient;
    }

    public async Task<SharedPlayRoomSession> CreateSessionAndUploadAsync(
        Uri cdnBaseUri,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var response = await RequestCreateSessionAsync(
            SharedPlayDefaults.BuildEndpoint(cdnBaseUri, "/shared-play/v2/sessions"),
            package,
            playback,
            cancellationToken);

        var roomCode = response.RoomCode?.Trim();
        if (string.IsNullOrWhiteSpace(roomCode))
            throw new InvalidOperationException("The CDN did not return a Shared Play room code.");

        var stateUri = BuildSessionEndpoint(cdnBaseUri, roomCode, "state");
        var queueUri = BuildSessionEndpoint(cdnBaseUri, roomCode, "queue");
        var joinUri = SharedPlayDefaults.BuildWebShareJoinUrl(cdnBaseUri, roomCode);

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static u => string.Equals(u.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        return new SharedPlayRoomSession(
            roomCode,
            SharedPlayDefaults.DisplayRoomCode(roomCode),
            joinUri.ToString(),
            string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            stateUri,
            queueUri,
            response.ExpiresAtUtc);
    }

    public async Task<SharedPlayRoomSession> UploadTrackToSessionAsync(
        Uri cdnBaseUri,
        SharedPlayRoomSession session,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var response = await RequestCreateSessionAsync(
            BuildSessionEndpoint(cdnBaseUri, session.RoomCode, "tracks"),
            package,
            playback,
            cancellationToken);

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static u => string.Equals(u.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        return session with
        {
            TrackId = string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            ExpiresAtUtc = response.ExpiresAtUtc ?? session.ExpiresAtUtc
        };
    }

    public async Task<SharedPlayPreparedTrack> PrepareTrackInSessionAsync(
        Uri cdnBaseUri,
        SharedPlayRoomSession session,
        string fileKey,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var response = await RequestCreateSessionAsync(
            BuildSessionEndpoint(cdnBaseUri, session.RoomCode, "tracks"),
            package,
            playback,
            cancellationToken,
            activateOnUpload: false);

        var uploadTarget = response.Uploads?
            .FirstOrDefault(static u => string.Equals(u.Name, "spectralis-package", StringComparison.OrdinalIgnoreCase))
            ?? response.Uploads?.FirstOrDefault();

        if (uploadTarget is null)
            throw new InvalidOperationException("The CDN did not return an upload URL for the Shared Play package.");

        await UploadPackageAsync(uploadTarget, package, cancellationToken);

        var trackKey = TrackAssetKey(package.TrackId);
        var packageUrl = TryCreateHttpsUri(uploadTarget.AssetUrl, out var parsedPackageUri)
            ? parsedPackageUri
            : new Uri(cdnBaseUri, $"shared-play/v2/sessions/{Uri.EscapeDataString(session.RoomCode)}/tracks/{Uri.EscapeDataString(trackKey)}/package");

        return new SharedPlayPreparedTrack(
            fileKey,
            string.IsNullOrWhiteSpace(response.TrackId) ? package.TrackId : response.TrackId,
            packageUrl,
            package.Track,
            DateTimeOffset.UtcNow);
    }

    public async Task<SharedPlayRoomSession> ActivatePreparedTrackAsync(
        Uri cdnBaseUri,
        SharedPlayRoomSession session,
        SharedPlayPreparedTrack preparedTrack,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var trackKey = TrackAssetKey(preparedTrack.TrackId);
        var endpoint = new Uri(cdnBaseUri,
            $"shared-play/v2/sessions/{Uri.EscapeDataString(session.RoomCode)}/tracks/{Uri.EscapeDataString(trackKey)}/activate");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                roomCode = session.RoomCode,
                trackId = preparedTrack.TrackId,
                activeTrackId = preparedTrack.TrackId,
                playback = playback with { TrackId = preparedTrack.TrackId }
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play prepared track activation", cancellationToken);

        return session with { TrackId = preparedTrack.TrackId };
    }

    public async Task<(string RoomCode, string? TrackId, Uri StateUrl, Uri QueueUrl, Uri PackageUrl, DateTimeOffset? ExpiresAtUtc)>
        FetchSessionAsync(
            Uri cdnBaseUri,
            string roomCodeInput,
            CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");

        var roomCode = SharedPlayDefaults.NormalizeRoomCode(roomCodeInput)
            ?? throw new InvalidOperationException("Shared Play room code was invalid.");

        var endpoint = new Uri(cdnBaseUri, $"shared-play/v2/sessions/{Uri.EscapeDataString(roomCode)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play session fetch", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadFetchedSession(document.RootElement, cdnBaseUri, roomCode);
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
            if (read == 0) break;
            totalBytes += read;
            if (totalBytes > SharedPlayDefaults.MaxPackageBytes)
                throw new InvalidOperationException("Shared Play package is larger than the download safety limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    public async Task PublishPlaybackStateAsync(
        SharedPlayRoomSession session,
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
                roomCode = session.RoomCode,
                trackId = session.TrackId,
                activeTrackId = session.TrackId,
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
        SharedPlayRoomSession session,
        SharedPlayQueueSnapshot queue,
        CancellationToken cancellationToken)
    {
        EnsureHttps(session.QueueUrl, "Shared Play queue URL");

        using var request = new HttpRequestMessage(HttpMethod.Post, session.QueueUrl)
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                roomCode = session.RoomCode,
                queue
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play queue publish", cancellationToken);
    }

    // ─── Streamer Queue ────────────────────────────────────────────────────────

    public async Task<StreamerQueueState?> GetStreamerQueueAsync(
        Uri cdnBaseUri,
        string roomCode,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = BuildSessionEndpoint(cdnBaseUri, roomCode, "streamer-queue");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Get streamer queue", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<StreamerQueueState>(stream, JsonOptions, cancellationToken);
    }

    public async Task PutStreamerQueueSettingsAsync(
        Uri cdnBaseUri,
        string roomCode,
        string sessionKey,
        bool enabled,
        StreamerQueueSettings settings,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = BuildSessionEndpoint(cdnBaseUri, roomCode, "streamer-queue/settings");

        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = JsonContent(new StreamerQueuePutRequest
            {
                SessionKey = sessionKey,
                Enabled = enabled,
                Settings = settings
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Put streamer queue settings", cancellationToken);
    }

    public async Task<string?> GetStripeConnectUrlAsync(
        Uri cdnBaseUri,
        string channelId,
        string ownerToken,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = SharedPlayDefaults.BuildEndpoint(cdnBaseUri,
            $"/shared-play/v2/channels/{Uri.EscapeDataString(channelId)}/stripe/connect?ownerToken={Uri.EscapeDataString(ownerToken)}");

        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Get Stripe Connect URL", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<StreamerQueueStripeConnectResponse>(stream, JsonOptions, cancellationToken);
        return result?.Url;
    }

    public async Task ApproveSubmissionAsync(
        Uri cdnBaseUri,
        string roomCode,
        string sessionKey,
        string itemId,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = BuildSessionEndpoint(cdnBaseUri, roomCode,
            $"streamer-queue/items/{Uri.EscapeDataString(itemId)}/approve");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(new { sessionKey })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Approve submission", cancellationToken);
    }

    public async Task RejectSubmissionAsync(
        Uri cdnBaseUri,
        string roomCode,
        string sessionKey,
        string itemId,
        CancellationToken cancellationToken)
    {
        EnsureHttps(cdnBaseUri, "CDN base URL");
        var endpoint = BuildSessionEndpoint(cdnBaseUri, roomCode,
            $"streamer-queue/items/{Uri.EscapeDataString(itemId)}/reject");

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(new { sessionKey })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Reject submission", cancellationToken);
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
            $"/shared-play/v2/channels/{Uri.EscapeDataString(channelId)}");

        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = JsonContent(payload)
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play live channel publish", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedPlayChannelResponse>(stream, JsonOptions, cancellationToken);
    }

    private async Task<SharedPlayCreateSessionResponse> RequestCreateSessionAsync(
        Uri endpoint,
        SharedPlayPackage package,
        SharedPlayPlaybackSnapshot playback,
        CancellationToken cancellationToken,
        bool activateOnUpload = true)
    {
        var playbackWithTrack = playback with { TrackId = package.TrackId };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent(new
            {
                protocolVersion = SharedPlayDefaults.ProtocolVersion,
                clientName = SharedPlayDefaults.ClientName,
                packageKind = "spectralis-rich",
                track = package.Track,
                package = new SharedPlayPackageDescriptor(
                    package.TrackId,
                    package.AudioSha256,
                    package.PackageSha256,
                    package.AudioBytes,
                    package.PackageBytes,
                    package.AudioExtension,
                    SharedPlayDefaults.RichPackageContentType),
                playback = playbackWithTrack,
                capabilities = SharedPlayCacheStore.CreateCapabilities(),
                activateOnUpload
            })
        };

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play create session request", cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<SharedPlayCreateSessionResponse>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The CDN returned an empty Shared Play session response.");
    }

    private static (string RoomCode, string? TrackId, Uri StateUrl, Uri QueueUrl, Uri PackageUrl, DateTimeOffset? ExpiresAtUtc)
        ReadFetchedSession(JsonElement payload, Uri cdnBaseUri, string fallbackRoomCode)
    {
        var source = TryGetObject(payload, "session", out var sessionElement) ? sessionElement : payload;

        var roomCode = FirstString(source, "roomCode") ??
            FirstString(payload, "roomCode") ??
            fallbackRoomCode;

        var trackId = FirstString(source, "activeTrackId", "currentTrackId", "trackId") ??
            FirstString(payload, "activeTrackId", "currentTrackId", "trackId");

        var encodedCode = Uri.EscapeDataString(roomCode);
        var stateUrl = FirstUrl(cdnBaseUri,
            FirstString(source, "stateUrl"),
            FirstNestedString(source, "links", "state"),
            FirstString(payload, "stateUrl"),
            $"/shared-play/v2/sessions/{encodedCode}/state")
            ?? throw new InvalidOperationException("The CDN session response did not include a valid state URL.");

        var packageUrl = FirstUrl(cdnBaseUri,
            FirstString(source, "packageUrl"),
            FirstString(source, "spectralisPackageUrl"),
            FirstNestedString(source, "package", "assetUrl"),
            FirstNestedString(source, "package", "url"),
            $"/shared-play/v2/sessions/{encodedCode}/package")
            ?? throw new InvalidOperationException("The CDN session response did not include a valid package URL.");

        var queueUrl = FirstUrl(cdnBaseUri,
            FirstString(source, "queueUrl"),
            FirstNestedString(source, "links", "queue"),
            FirstString(payload, "queueUrl"),
            $"/shared-play/v2/sessions/{encodedCode}/queue")
            ?? throw new InvalidOperationException("The CDN session response did not include a valid queue URL.");

        return (roomCode, trackId, stateUrl, queueUrl, packageUrl,
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

        using var request = new HttpRequestMessage(method, uploadUri) { Content = content };
        ApplyUploadHeaders(request, uploadTarget.Headers);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, "Shared Play package upload", cancellationToken);
    }

    private static Uri BuildSessionEndpoint(Uri cdnBaseUri, string roomCode, string segment)
    {
        return new Uri(cdnBaseUri, $"shared-play/v2/sessions/{Uri.EscapeDataString(roomCode)}/{segment}");
    }

    private static void ApplyUploadHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var (name, value) in headers)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(value)) continue;
            if (IsRestrictedHeader(name, value)) continue;
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

        if (TryGetObject(payload, "playback", out var playbackElement) && HasPlaybackFields(playbackElement))
        {
            playback = ReadPlaybackSnapshot(playbackElement, payloadTrackId);
            return true;
        }
        if (TryGetObject(payload, "state", out var stateElement))
        {
            if (TryGetObject(stateElement, "playback", out var sp) && HasPlaybackFields(sp))
            {
                playback = ReadPlaybackSnapshot(sp, payloadTrackId);
                return true;
            }
            if (HasPlaybackFields(stateElement))
            {
                playback = ReadPlaybackSnapshot(stateElement, payloadTrackId);
                return true;
            }
        }
        if (TryGetObject(payload, "session", out var sessionEl) &&
            TryGetObject(sessionEl, "playback", out var sp2) && HasPlaybackFields(sp2))
        {
            playback = ReadPlaybackSnapshot(sp2, payloadTrackId);
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
            if (TryCreateHttpsUri(cdnBaseUri, value, out var uri)) return uri;
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

    private static string? FirstNestedString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var prop in path)
        {
            if (!TryGetObjectProperty(current, prop, out current)) return null;
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    private static string? FirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGetObjectProperty(element, name, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text.Trim();
            }
        }
        return null;
    }

    private static double? FirstDouble(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetObjectProperty(element, name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var n)) return n;
            if (value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), out var p)) return p;
        }
        return null;
    }

    private static bool? FirstBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!TryGetObjectProperty(element, name, out var value)) continue;
            if (value.ValueKind is JsonValueKind.True or JsonValueKind.False) return value.GetBoolean();
            if (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var p)) return p;
        }
        return null;
    }

    private static DateTimeOffset? FirstDateTimeOffset(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = FirstString(element, name);
            if (DateTimeOffset.TryParse(value, out var parsed)) return parsed;
        }
        return null;
    }

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value) =>
        TryGetObjectProperty(element, name, out value) && value.ValueKind == JsonValueKind.Object;

    private static bool TryGetObjectProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }

    private static string TrackAssetKey(string trackId)
    {
        var chars = trackId.Trim()
            .Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' ? c : '-')
            .ToArray();
        var normalized = new string(chars).Trim('-');
        return string.IsNullOrWhiteSpace(normalized)
            ? Guid.NewGuid().ToString("N")
            : normalized[..Math.Min(96, normalized.Length)];
    }

    private static HttpContent JsonContent<T>(T value) =>
        new StringContent(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var detail = "";
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body)) detail = $" Response: {body.Trim()}";
        }
        catch { }

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

    public void Dispose() => httpClient.Dispose();
}
