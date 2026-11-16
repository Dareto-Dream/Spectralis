using System.Text.RegularExpressions;

namespace Spectralis.App.Services;

/// <summary>Publishes recognized links immediately; enrichment never holds up clipboard polling.</summary>
internal sealed class ClipboardLinkPreview : IDisposable
{
    private static readonly Regex Links = new("(?:https?://[^\\s<>()\"']+|spotify:(?:track|album|playlist|artist|episode):[A-Za-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly Func<string, CancellationToken, Task<RemoteAudioResolveResult>> _resolve;
    private readonly Func<string?, CancellationToken, Task<byte[]?>> _artwork;
    private readonly Action<string, RemoteAudioResolveResult> _show;
    private readonly Func<string, RemoteAudioResolveResult, byte[]?, bool> _update;
    private readonly Action<Exception> _onError;
    private readonly HashSet<string> _shownLinks = new(StringComparer.Ordinal);
    private CancellationTokenSource? _pending;
    private string? _lastText;
    private bool _disposed;

    public ClipboardLinkPreview(Action<string, RemoteAudioResolveResult> show,
        Func<string, RemoteAudioResolveResult, byte[]?, bool> update,
        Func<string, CancellationToken, Task<RemoteAudioResolveResult>> resolve,
        Func<string?, CancellationToken, Task<byte[]?>> artwork, Action<Exception> onError)
        => (_show, _update, _resolve, _artwork, _onError) = (show, update, resolve, artwork, onError);

    public Task ObserveAsync(string? text)
    {
        if (_disposed || string.Equals(text, _lastText, StringComparison.Ordinal)) return Task.CompletedTask;
        _lastText = text;
        Cancel();
        if (string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;

        // Prefer a whole copied URL, but also recognize a supported URL in a message.
        var wholeLink = text.Trim().Any(char.IsWhiteSpace) ? Array.Empty<string>() : new[] { text.Trim() };
        foreach (var candidate in wholeLink.Concat(Links.Matches(text).Select(match =>
                     match.Value.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}', '"', '\''))).Take(16))
        {
            if (!OpenUrlService.TryCreateClipboardPreview(candidate, out var preview)) continue;
            if (!_shownLinks.Add(OpenUrlService.ClipboardLinkIdentity(preview))) continue;
            _pending = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var token = _pending.Token;
            _show(preview.SourceUrl, preview);
            return EnrichAsync(preview.SourceUrl, token);
        }
        return Task.CompletedTask;
    }

    private async Task EnrichAsync(string url, CancellationToken token)
    {
        try
        {
            var resolved = await _resolve(url, token);
            token.ThrowIfCancellationRequested();
            if (!_update(url, resolved, null)) return;
            if (string.IsNullOrWhiteSpace(resolved.ArtworkUrl)) return;
            var artwork = await _artwork(resolved.ArtworkUrl, token);
            token.ThrowIfCancellationRequested();
            if (artwork is { Length: > 0 }) _update(url, resolved, artwork);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception error) { if (!token.IsCancellationRequested) _onError(error); }
    }

    public void Cancel()
    {
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }

    public void Dispose() { _disposed = true; Cancel(); }
}
