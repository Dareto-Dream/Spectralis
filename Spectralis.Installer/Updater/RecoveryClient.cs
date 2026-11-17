using System.Security.Cryptography;
using System.Text.Json;

namespace Spectralis.Updater;

public sealed record RecoveryRelease(string Version, Uri Installer, string Sha256 = "", long Size = 0);

public sealed class RecoveryClient(HttpClient http)
{
    public const string CatalogUrl = "https://spectralis-api.deltavdevs.com/spectralis/v1/releases";
    private const string DownloadPrefix = "/Dareto-Dream/Spectralis/releases/download/";
    public const long MaximumInstallerSize = 2L * 1024 * 1024 * 1024;

    public static bool IsTrustedInstaller(Uri uri, string rid) =>
        uri.Scheme == "https" && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsolutePath.StartsWith(DownloadPrefix, StringComparison.Ordinal) &&
        uri.AbsolutePath[DownloadPrefix.Length..].Split('/') is [var tag, var file] && tag.Length > 0 &&
        file == (rid switch
        {
            "win-x64" => "Spectralis-win-x64-Setup.exe",
            "linux-x64" => "Spectralis-linux-x64.AppImage",
            "osx-x64" => "Spectralis-osx-x64-Setup.pkg",
            "osx-arm64" => "Spectralis-osx-arm64-Setup.pkg",
            _ => ""
        });

    public static RecoveryRelease SelectLatest(string json, string rid)
    {
        if (rid is not ("win-x64" or "linux-x64" or "osx-x64" or "osx-arm64")) throw new PlatformNotSupportedException();
        using var document = JsonDocument.Parse(json);
        RecoveryRelease? best = null;
        Version? newest = null;
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Text(item, "channel") != "stable" ||
                !Version.TryParse(Text(item, "version"), out var version) ||
                !item.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Object ||
                !platforms.TryGetProperty(rid, out var platform) ||
                !Uri.TryCreate(Text(platform, "installer"), UriKind.Absolute, out var uri) || !IsTrustedInstaller(uri, rid)) continue;
            if (newest is null || version > newest)
            {
                newest = version;
                best = new(Text(item, "version"), uri);
            }
        }
        return best ?? throw new InvalidOperationException("No stable installer is available for this platform.");
    }

    public async Task<RecoveryRelease> FindLatestAsync(string rid, CancellationToken ct)
    {
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Spectralis-Updater/1.0");
        var release = SelectLatest(await http.GetStringAsync(CatalogUrl, ct), rid);
        var parts = release.Installer.AbsolutePath[DownloadPrefix.Length..].Split('/');
        var tag = Uri.UnescapeDataString(parts[0]);
        if (tag != release.Version && tag != "v" + release.Version)
            throw new InvalidDataException("The installer tag does not match its version.");
        using var metadata = JsonDocument.Parse(await http.GetStringAsync(
            "https://api.github.com/repos/Dareto-Dream/Spectralis/releases/tags/" + Uri.EscapeDataString(tag), ct));
        foreach (var asset in metadata.RootElement.GetProperty("assets").EnumerateArray())
        {
            if (Text(asset, "browser_download_url") != release.Installer.AbsoluteUri) continue;
            var digest = Text(asset, "digest");
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                !digest[7..].All(Uri.IsHexDigit) || !asset.TryGetProperty("size", out var size) ||
                !size.TryGetInt64(out var bytes) || bytes <= 0 || bytes > MaximumInstallerSize) break;
            return release with { Sha256 = digest[7..], Size = bytes };
        }
        throw new InvalidDataException("The release does not have a verified installer. Download it from spectralis.deltavdevs.com/setup.");
    }

    public async Task DownloadAsync(RecoveryRelease release, string destination, IProgress<long>? progress, CancellationToken ct)
    {
        if (release.Sha256.Length != 64 || release.Size <= 0 || release.Size > MaximumInstallerSize)
            throw new InvalidDataException("Installer verification information is missing.");
        var created = false;
        try
        {
            using var response = await http.GetAsync(release.Installer, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != release.Size)
                throw new InvalidDataException("The installer size does not match the release.");
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                created = true;
                var buffer = new byte[128 * 1024];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) != 0)
                {
                    total += read;
                    if (total > release.Size) throw new InvalidDataException("The installer is larger than expected.");
                    hash.AppendData(buffer.AsSpan(0, read));
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    progress?.Report(total);
                }
                if (total != release.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(release.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Installer verification failed. Nothing was installed.");
            }
        }
        catch
        {
            if (created) try { File.Delete(destination); } catch { }
            throw;
        }
    }

    private static string Text(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
}
