using System.Text.Json;
using System.Threading.Channels;
using Spectralis.Core.Platform;

namespace Spectralis.App.Services;

/// <summary>Transfers playback audio from Untitled's browser session into the native audio cache.</summary>
public sealed class UntitledBrowserCacheSession : IDisposable
{
    private readonly IWebViewHost _host;
    private readonly Uri _source;
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _cancel = new();
    private readonly Channel<string> _messages = Channel.CreateBounded<string>(8);
    private readonly Task _transfer;
    private bool _disposed;

    public UntitledBrowserCacheSession(IWebViewHost host, Uri source)
    {
        if (source.Scheme != "https" || source.Host != "untitled.stream" ||
            !source.AbsolutePath.StartsWith("/library/track/", StringComparison.Ordinal))
            throw new ArgumentException("An Untitled track page is required.", nameof(source));
        _host = host;
        _source = new UriBuilder(source) { Fragment = "", Query = "" }.Uri;
        _host.MessageReceived += OnMessage;
        _transfer = TransferAsync();
    }

    public event EventHandler<RemoteAudioResolveResult>? Completed;
    public event EventHandler<string>? Failed;
    public Task Completion => _transfer;

    public Task StartAsync()
    {
        if (_disposed || _transfer.IsCompleted) return Task.CompletedTask;
        using var resource = typeof(UntitledBrowserCacheSession).Assembly
            .GetManifestResourceStream("Spectralis.App.Assets.untitled-cache.js")!;
        using var reader = new StreamReader(resource);
        return _host.ExecuteScriptAsync(reader.ReadToEnd() +
            $"\nwindow.spectralisCacheUntitled({JsonSerializer.Serialize(_source.AbsoluteUri)}, {JsonSerializer.Serialize(_session)});");
    }

    private void OnMessage(object? sender, string message)
    {
        if (_disposed || message.Length > 64 * 1024) return;
        try
        {
            using var document = JsonDocument.Parse(message);
            var root = document.RootElement;
            if (root.GetProperty("type").GetString() != "untitled-cache" ||
                root.GetProperty("session").GetString() != _session) return;
            if (!_messages.Writer.TryWrite(message)) _cancel.Cancel();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
    }

    private async Task TransferAsync()
    {
        string? path = null;
        FileStream? output = null;
        RemoteAudioResolveResult? result = null;
        long? expectedBytes = null;
        var sequence = 0;
        try
        {
            await foreach (var message in _messages.Reader.ReadAllAsync(_cancel.Token))
            {
                using var document = JsonDocument.Parse(message);
                var root = document.RootElement;
                var kind = root.GetProperty("kind").GetString();
                if (kind is "error" or "reset")
                {
                    // A signing/audio request can be challenged before the page is cleared.
                    // Keep the receiver alive so a later readiness probe can restart at chunk zero.
                    if (output is not null) await output.DisposeAsync();
                    output = null;
                    RemoteAudioCache.TryDelete(path);
                    path = null;
                    result = null;
                    expectedBytes = null;
                    sequence = 0;
                    if (kind == "reset") continue;
                    if (!root.TryGetProperty("retryable", out var retryable) || retryable.ValueKind != JsonValueKind.True)
                        throw new InvalidDataException("Untitled browser could not cache playback audio.");
                    var stage = Text(root, "stage") is "track" or "signing" or "audio" or "transfer"
                        ? Text(root, "stage") : "unknown stage";
                    var status = root.TryGetProperty("httpStatus", out var httpStatus) && httpStatus.TryGetInt32(out var code)
                        ? $" (HTTP {code})" : "";
                    var errorName = Text(root, "errorName") is "TypeError" or "TimeoutError" or "AbortError" or "InvalidStateError"
                        ? Text(root, "errorName") : "Error";
                    Failed?.Invoke(this, $"Untitled browser cache attempt failed during {stage}{status}: {errorName}; waiting to retry.");
                    continue;
                }
                if (root.GetProperty("sequence").GetInt32() != sequence)
                    throw new InvalidDataException("Untitled cache chunks arrived out of order.");
                switch (kind)
                {
                    case "begin" when output is null:
                        if (root.GetProperty("sourceUrl").GetString() != _source.AbsoluteUri)
                            throw new InvalidDataException("Untitled cache belongs to a different track.");
                        if (root.TryGetProperty("expectedBytes", out var length) && length.ValueKind == JsonValueKind.Number)
                        {
                            expectedBytes = length.GetInt64();
                            if (expectedBytes is <= 0 or > 256 * 1024 * 1024)
                                throw new InvalidDataException("Untitled audio size is invalid.");
                        }
                        path = RemoteAudioCache.CreateDownloadTemplate(out _).Replace(".%(ext)s", ".mp3");
                        output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                            64 * 1024, useAsync: true);
                        var duration = root.GetProperty("duration").GetDouble();
                        result = new RemoteAudioResolveResult(RemoteAudioServiceKind.Untitled, "Untitled",
                            _source.AbsoluteUri, "", ".mp3", "Untitled MP3",
                            Text(root, "title"), Text(root, "artist"), "Untitled", Artwork(root),
                            double.IsFinite(duration) && duration is > 0 and < 86400 ? TimeSpan.FromSeconds(duration) : TimeSpan.Zero,
                            _source.AbsoluteUri, CachedAudioPath: path);
                        break;
                    case "chunk" when output is not null:
                        var bytes = Convert.FromBase64String(root.GetProperty("data").GetString()!);
                        if (bytes.Length is 0 or > 24 * 1024 || output.Length + bytes.Length > 256 * 1024 * 1024)
                            throw new InvalidDataException("Untitled cache chunk size is invalid.");
                        await output.WriteAsync(bytes, _cancel.Token);
                        break;
                    case "end" when output is not null:
                        if (output.Length == 0 || output.Length != root.GetProperty("bytes").GetInt64() ||
                            (expectedBytes.HasValue && output.Length != expectedBytes.Value))
                            throw new InvalidDataException("Untitled audio download was incomplete.");
                        await output.DisposeAsync();
                        output = null;
                        await AckAsync(sequence);
                        _cancel.Token.ThrowIfCancellationRequested();
                        var completed = Completed;
                        if (completed is null) return;
                        completed.Invoke(this, result!);
                        path = null; // The playback loader now owns the cached file.
                        return;
                    default:
                        throw new InvalidDataException("Unexpected Untitled cache message.");
                }
                await AckAsync(sequence++);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
        finally
        {
            if (output is not null) await output.DisposeAsync();
            RemoteAudioCache.TryDelete(path);
        }
    }

    private Task AckAsync(int sequence) => _host.ExecuteScriptAsync(
        $"window.spectralisUntitledCacheAck && window.spectralisUntitledCacheAck({JsonSerializer.Serialize(_session)}, {sequence});");

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?[..Math.Min(value.GetString()!.Length, 1024)] : null;

    private static string? Artwork(JsonElement root) =>
        Uri.TryCreate(Text(root, "artworkUrl"), UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        (uri.Host == "untitled.stream" || uri.Host.EndsWith(".untitled.stream", StringComparison.OrdinalIgnoreCase))
            ? uri.AbsoluteUri : null;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.MessageReceived -= OnMessage;
        _cancel.Cancel();
        _messages.Writer.TryComplete();
        _ = _transfer.ContinueWith(_ => _cancel.Dispose(), TaskScheduler.Default);
    }
}
