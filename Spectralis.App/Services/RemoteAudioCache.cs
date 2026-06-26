using System.Net.Http.Headers;

namespace Spectralis.App.Services;

internal static class RemoteAudioCache
{
    private static readonly HttpClient Http = CreateHttpClient();
    private static readonly string CacheRoot = Path.Combine(
        Path.GetTempPath(),
        "Spectralis",
        "remote-audio");

    public static string CreateDownloadTemplate(out string token)
    {
        Directory.CreateDirectory(CacheRoot);
        token = Guid.NewGuid().ToString("N");
        return Path.Combine(CacheRoot, token + ".%(ext)s");
    }

    public static string? FindDownloadedFile(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !Directory.Exists(CacheRoot))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(CacheRoot, token + ".*", SearchOption.TopDirectoryOnly)
                .OrderByDescending(static path => new FileInfo(path).Length)
                .FirstOrDefault(static path => new FileInfo(path).Length > 0);
        }
        catch
        {
            return null;
        }
    }

    public static async Task<string> DownloadAsync(
        string url,
        string extension,
        CancellationToken cancellationToken,
        bool requestInitialRange = false,
        string? referer = null,
        IProgress<int>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("Remote audio URL is required.", nameof(url));
        }

        if (!extension.StartsWith('.'))
        {
            extension = "." + extension;
        }

        Directory.CreateDirectory(CacheRoot);
        var path = Path.Combine(CacheRoot, $"{Guid.NewGuid():N}{extension}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("*/*");
            if (requestInitialRange)
            {
                request.Headers.Range = new RangeHeaderValue(0, null);
            }

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

            var contentLength = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.Read,
                128 * 1024,
                useAsync: true);

            if (progress is null || contentLength is null or <= 0)
            {
                await source.CopyToAsync(target, cancellationToken);
            }
            else
            {
                var buffer = new byte[128 * 1024];
                long totalRead = 0;
                int lastReported = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    totalRead += read;
                    var pct = (int)(totalRead * 100L / contentLength.Value);
                    if (pct != lastReported)
                    {
                        progress.Report(pct);
                        lastReported = pct;
                    }
                }
            }

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
        {
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullRoot = Path.GetFullPath(CacheRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }
        catch
        {
            // Cache cleanup should never interrupt playback shutdown.
        }
    }

    /// <summary>Total bytes currently held in the remote audio cache.</summary>
    public static long GetCacheSizeBytes()
    {
        try
        {
            if (!Directory.Exists(CacheRoot))
            {
                return 0;
            }

            return Directory
                .EnumerateFiles(CacheRoot, "*", SearchOption.TopDirectoryOnly)
                .Sum(path => new FileInfo(path).Length);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>Deletes every cached remote file; in-use files are skipped. Returns bytes freed.</summary>
    public static long ClearAll()
    {
        long freed = 0;
        try
        {
            if (!Directory.Exists(CacheRoot))
            {
                return 0;
            }

            foreach (var path in Directory.EnumerateFiles(CacheRoot, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var size = new FileInfo(path).Length;
                    File.Delete(path);
                    freed += size;
                }
                catch
                {
                    // Locked by active playback; leave it.
                }
            }
        }
        catch
        {
        }

        return freed;
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        return http;
    }
}
