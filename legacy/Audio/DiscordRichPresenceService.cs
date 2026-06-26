using System.Diagnostics;
using System.Reflection;
using System.Text;
using DiscordRPC;

namespace Spectralis;

internal sealed class DiscordRichPresenceService : IDisposable
{
    private const string ApplicationIdMetadataName = "DiscordApplicationId";
    private const string ApplicationIdEnvironmentVariable = "SPECTRALIS_DISCORD_CLIENT_ID";
    private const string DownloadUrl = "https://www.deltavdevs.com/projects/spectralis";
    private const string DownloadButtonLabel = "Download Spectralis";
    private const string ListenTogetherButtonLabel = "Listen Together";
    private const int MaxPresenceTextBytes = 128;
    private const int MaxButtonUrlCharacters = 512;
    private const int InitializeRetryDelayMilliseconds = 30_000;
    private const double PositionResyncThresholdSeconds = 3;

    private DiscordRpcClient? client;
    private bool enabled = true;
    private bool applicationIdMissing;
    private long nextInitializeAttemptTick;
    private string? lastPresenceSignature;
    private double lastSentPositionSeconds;
    private DateTime lastSentAtUtc;
    private bool disposed;

    public void SetEnabled(bool enabled)
    {
        if (this.enabled == enabled)
        {
            return;
        }

        this.enabled = enabled;

        if (!enabled)
        {
            ClearPresence();
            return;
        }

        lastPresenceSignature = null;
    }

    public void Update(
        AudioTrackInfo? track,
        bool isPlaying,
        TimeSpan position,
        TimeSpan length,
        string? sharedPlayJoinUrl = null,
        int queuePosition = 0,
        int queueCount = 0)
    {
        if (disposed)
        {
            return;
        }

        if (!enabled)
        {
            return;
        }

        if (track is null)
        {
            ClearPresence();
            return;
        }

        if (!EnsureClient())
        {
            return;
        }

        var nowUtc = DateTime.UtcNow;
        var normalizedPosition = NormalizePosition(position, length);
        var normalizedLength = NormalizeLength(length, track.Duration);
        var normalizedSharedPlayJoinUrl = NormalizeButtonUrl(sharedPlayJoinUrl);
        var signature = BuildPresenceSignature(
            track,
            isPlaying,
            normalizedPosition,
            normalizedLength,
            normalizedSharedPlayJoinUrl,
            queuePosition,
            queueCount);

        if (signature == lastPresenceSignature && !NeedsPositionResync(isPlaying, normalizedPosition, nowUtc))
        {
            return;
        }

        try
        {
            client?.SetPresence(BuildPresence(
                track,
                isPlaying,
                normalizedPosition,
                normalizedLength,
                nowUtc,
                normalizedSharedPlayJoinUrl,
                queuePosition,
                queueCount));
            lastPresenceSignature = signature;
            lastSentPositionSeconds = normalizedPosition.TotalSeconds;
            lastSentAtUtc = nowUtc;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord rich presence update failed: {ex}");
            ResetClient();
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        ClearPresence();
        ResetClient();
    }

    private bool EnsureClient()
    {
        if (client?.IsInitialized == true)
        {
            return true;
        }

        if (applicationIdMissing)
        {
            return false;
        }

        var nowTick = Environment.TickCount64;
        if (nowTick < nextInitializeAttemptTick)
        {
            return false;
        }

        nextInitializeAttemptTick = nowTick + InitializeRetryDelayMilliseconds;

        var applicationId = ResolveApplicationId();
        if (string.IsNullOrWhiteSpace(applicationId))
        {
            applicationIdMissing = true;
            Debug.WriteLine(
                $"Discord rich presence is enabled, but no application ID was found. Set {ApplicationIdEnvironmentVariable} or the {ApplicationIdMetadataName} assembly metadata value.");
            return false;
        }

        try
        {
            ResetClient();
            client = new DiscordRpcClient(applicationId.Trim())
            {
                SkipIdenticalPresence = true
            };

            return client.Initialize();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord rich presence initialization failed: {ex}");
            ResetClient();
            return false;
        }
    }

    private void ClearPresence()
    {
        if (client?.IsInitialized != true || lastPresenceSignature is null)
        {
            lastPresenceSignature = null;
            return;
        }

        try
        {
            client.ClearPresence();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord rich presence clear failed: {ex}");
        }
        finally
        {
            lastPresenceSignature = null;
        }
    }

    private void ResetClient()
    {
        try
        {
            client?.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Discord rich presence disposal failed: {ex}");
        }
        finally
        {
            client = null;
            lastPresenceSignature = null;
        }
    }

    private bool NeedsPositionResync(bool isPlaying, TimeSpan position, DateTime nowUtc)
    {
        if (!isPlaying || lastPresenceSignature is null)
        {
            return false;
        }

        var expectedPositionSeconds = lastSentPositionSeconds + (nowUtc - lastSentAtUtc).TotalSeconds;
        return Math.Abs(position.TotalSeconds - expectedPositionSeconds) > PositionResyncThresholdSeconds;
    }

    private static RichPresence BuildPresence(
        AudioTrackInfo track,
        bool isPlaying,
        TimeSpan position,
        TimeSpan length,
        DateTime nowUtc,
        string? sharedPlayJoinUrl,
        int queuePosition,
        int queueCount)
    {
        var presence = new RichPresence
        {
            Type = ActivityType.Listening,
            StatusDisplay = StatusDisplayType.Details,
            Details = ClampPresenceText(track.DisplayName, "Untitled track"),
            State = ClampPresenceText(BuildStateText(track, isPlaying, position, queuePosition, queueCount), "Local audio"),
            Buttons = BuildButtons(sharedPlayJoinUrl),
            Party = BuildParty(sharedPlayJoinUrl)
        };

        if (isPlaying)
        {
            presence.Timestamps = BuildTimestamps(position, length, nowUtc);
        }

        return presence;
    }

    private static Timestamps BuildTimestamps(TimeSpan position, TimeSpan length, DateTime nowUtc)
    {
        var start = nowUtc - position;
        var remaining = length > TimeSpan.Zero ? length - position : TimeSpan.Zero;
        return remaining > TimeSpan.Zero
            ? new Timestamps(start, nowUtc + remaining)
            : new Timestamps(start);
    }

    private static string BuildStateText(AudioTrackInfo track, bool isPlaying, TimeSpan position, int queuePosition, int queueCount)
    {
        var queueSuffix = queueCount > 1 ? $"  ·  {queuePosition} of {queueCount}" : "";

        if (!isPlaying)
        {
            return $"Paused at {FormatTime(position)}{queueSuffix}";
        }

        if (!string.IsNullOrWhiteSpace(track.Artist) && !string.IsNullOrWhiteSpace(track.Album))
        {
            return $"{track.Artist.Trim()} - {track.Album.Trim()}{queueSuffix}";
        }

        if (!string.IsNullOrWhiteSpace(track.Artist))
        {
            return $"by {track.Artist.Trim()}{queueSuffix}";
        }

        if (!string.IsNullOrWhiteSpace(track.Album))
        {
            return $"from {track.Album.Trim()}{queueSuffix}";
        }

        return queueCount > 1 ? $"Local audio{queueSuffix}" : "Local audio";
    }

    private static string BuildPresenceSignature(
        AudioTrackInfo track,
        bool isPlaying,
        TimeSpan position,
        TimeSpan length,
        string? sharedPlayJoinUrl,
        int queuePosition,
        int queueCount)
    {
        var pausedPosition = isPlaying ? "" : $"|paused:{(int)position.TotalSeconds}";
        return string.Join(
            "|",
            track.FilePath,
            track.DisplayName,
            track.Artist,
            track.Album,
            isPlaying,
            (int)Math.Round(length.TotalSeconds),
            pausedPosition,
            sharedPlayJoinUrl ?? "",
            queuePosition,
            queueCount);
    }

    private static DiscordRPC.Button[] BuildButtons(string? sharedPlayJoinUrl)
    {
        var buttons = new List<DiscordRPC.Button>(2);

        if (!string.IsNullOrWhiteSpace(sharedPlayJoinUrl))
        {
            buttons.Add(new DiscordRPC.Button
            {
                Label = ListenTogetherButtonLabel,
                Url = sharedPlayJoinUrl
            });
        }

        buttons.Add(new DiscordRPC.Button
        {
            Label = DownloadButtonLabel,
            Url = DownloadUrl
        });

        return buttons.ToArray();
    }

    private static Party? BuildParty(string? sharedPlayJoinUrl)
    {
        if (string.IsNullOrWhiteSpace(sharedPlayJoinUrl))
        {
            return null;
        }

        return new Party
        {
            ID = CreatePartyId(sharedPlayJoinUrl),
            Size = 1,
            Max = 16,
            Privacy = Party.PrivacySetting.Public
        };
    }

    private static string CreatePartyId(string value)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return "spectralis-" + Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }

    private static TimeSpan NormalizePosition(TimeSpan position, TimeSpan length)
    {
        if (position < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return length > TimeSpan.Zero && position > length ? length : position;
    }

    private static TimeSpan NormalizeLength(TimeSpan engineLength, TimeSpan trackLength) =>
        engineLength > TimeSpan.Zero ? engineLength : trackLength;

    private static string ClampPresenceText(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        if (Encoding.UTF8.GetByteCount(text) <= MaxPresenceTextBytes)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var byteCount = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            if (byteCount + rune.Utf8SequenceLength > MaxPresenceTextBytes)
            {
                break;
            }

            builder.Append(rune);
            byteCount += rune.Utf8SequenceLength;
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static string? NormalizeButtonUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxButtonUrlCharacters)
        {
            return null;
        }

        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
            (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            ? uri.ToString()
            : null;
    }

    private static string FormatTime(TimeSpan duration)
    {
        duration = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
        return duration.TotalHours >= 1
            ? duration.ToString(@"h\:mm\:ss")
            : duration.ToString(@"m\:ss");
    }

    private static string? ResolveApplicationId()
    {
        var metadataValue = Assembly.GetEntryAssembly()
            ?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(static attribute =>
                string.Equals(attribute.Key, ApplicationIdMetadataName, StringComparison.OrdinalIgnoreCase))
            ?.Value;

        return FirstNonEmpty(
            metadataValue,
            Environment.GetEnvironmentVariable(ApplicationIdEnvironmentVariable));
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
