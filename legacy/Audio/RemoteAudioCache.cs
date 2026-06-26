using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Spectralis;

internal static class RemoteAudioCache
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string CacheRoot = Path.Combine(
        Path.GetTempPath(),
        "Spectralis",
        "remote-audio");

    public static async Task<string> DownloadAsync(
        string url,
        string extension,
        CancellationToken cancellationToken,
        bool requestInitialRange = false,
        string? referer = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("Remote audio URL is required.", nameof(url));

        if (!extension.StartsWith('.'))
            extension = "." + extension;

        Directory.CreateDirectory(CacheRoot);
        var path = Path.Combine(CacheRoot, $"{Guid.NewGuid():N}{extension}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
            request.Headers.Accept.ParseAdd("*/*");
            if (requestInitialRange)
                request.Headers.Range = new RangeHeaderValue(0, null);
            if (!string.IsNullOrWhiteSpace(referer) &&
                Uri.TryCreate(referer, UriKind.Absolute, out var refererUri))
            {
                request.Headers.Referrer = refererUri;
            }

            using var response = await Http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                128 * 1024,
                useAsync: true);
            await source.CopyToAsync(target, cancellationToken);

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    public static void TryDelete(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(CacheRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;

            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch
        {
            // Cache cleanup should never interrupt playback shutdown.
        }
    }

    public static void Clear()
    {
        try
        {
            if (!Directory.Exists(CacheRoot))
                return;

            var fullRoot = Path.GetFullPath(CacheRoot);
            foreach (var file in Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories))
            {
                TryDelete(file);
            }

            foreach (var directory in Directory
                .EnumerateDirectories(fullRoot, "*", SearchOption.AllDirectories)
                .OrderByDescending(static path => path.Length))
            {
                try
                {
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, recursive: false);
                }
                catch
                {
                }
            }
        }
        catch
        {
            // Cache cleanup should never interrupt playback shutdown.
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return http;
    }
}
