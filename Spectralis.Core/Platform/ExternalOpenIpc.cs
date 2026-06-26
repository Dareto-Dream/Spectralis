using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace Spectralis.Core.Platform;

public enum ExternalOpenKind
{
    File,
    Url,
    SharedPlay,
}

public enum ExternalOpenIntent
{
    Default,
    PlayNow,
    QueueNext,
    QueueEnd,
}

public sealed record ExternalOpenRequest(
    ExternalOpenKind Kind,
    string Value,
    string? CdnBaseUrl = null,
    ExternalOpenIntent Intent = ExternalOpenIntent.Default);

/// <summary>
/// Named-pipe handoff between a second launch and the running instance:
/// the new process forwards its open request and exits when accepted.
/// </summary>
public static class ExternalOpenIpc
{
    public const string PipeName = "Spectralis.PreserveSession.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static async Task<bool> TrySendAsync(
        ExternalOpenRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous,
                TokenImpersonationLevel.Identification);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(700));
            await pipe.ConnectAsync(timeout.Token);

            await using (var writer = new StreamWriter(pipe, leaveOpen: true))
            {
                await writer.WriteLineAsync(JsonSerializer.Serialize(request, JsonOptions).AsMemory(), timeout.Token);
                await writer.FlushAsync(timeout.Token);
            }

            pipe.WaitForPipeDrain();

            using var reader = new StreamReader(pipe, leaveOpen: true);
            var response = await reader.ReadLineAsync(timeout.Token);
            return string.Equals(response, "OK", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preserve session handoff failed: {ex}");
            return false;
        }
    }

    public static async Task<ExternalOpenRequest?> ReadRequestAsync(
        PipeStream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            using var reader = new StreamReader(pipe, leaveOpen: true);
            var line = await reader.ReadLineAsync(cancellationToken);
            return string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<ExternalOpenRequest>(line, JsonOptions);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Preserve session request read failed: {ex}");
            return null;
        }
    }

    /// <summary>Runs the receiving side: accepts handoffs and dispatches them until cancelled.</summary>
    public static async Task RunServerAsync(
        Func<ExternalOpenRequest, Task> handler,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                var request = await ReadRequestAsync(pipe, cancellationToken);
                if (request is not null)
                {
                    _ = handler(request);
                }

                var bytes = Encoding.UTF8.GetBytes("OK" + Environment.NewLine);
                await pipe.WriteAsync(bytes, cancellationToken);
                await pipe.FlushAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try
                {
                    await Task.Delay(250, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    /// <summary>Parses a spectralis://open?url=...&amp;intent=... protocol argument.</summary>
    public static ExternalOpenRequest? TryParseProtocolArgument(string? argument)
    {
        var value = argument?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "spectralis", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var pathParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(uri.Host))
        {
            pathParts.Add(uri.Host);
        }

        pathParts.AddRange(uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries));
        if (pathParts.Count == 0 ||
            !string.Equals(pathParts[0], "open", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var query = ParseQuery(uri.Query);
        if (!query.TryGetValue("url", out var url) || string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        return new ExternalOpenRequest(
            ExternalOpenKind.Url,
            url.Trim(),
            Intent: ParseIntent(query));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..eq]);
            var value = Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static ExternalOpenIntent ParseIntent(IReadOnlyDictionary<string, string> query)
    {
        if (query.TryGetValue("queue", out var queueFlag) &&
            (queueFlag == "1" || string.Equals(queueFlag, "true", StringComparison.OrdinalIgnoreCase)))
        {
            return ExternalOpenIntent.QueueNext;
        }

        if (!query.TryGetValue("intent", out var intent))
        {
            return ExternalOpenIntent.Default;
        }

        return intent.Trim().ToLowerInvariant() switch
        {
            "play" or "play-now" or "open" or "replace" => ExternalOpenIntent.PlayNow,
            "queue" or "queue-next" or "next" or "add-next" => ExternalOpenIntent.QueueNext,
            "queue-end" or "queue-last" or "end" or "append" or "add-end" => ExternalOpenIntent.QueueEnd,
            _ => ExternalOpenIntent.Default,
        };
    }
}
