using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using System.Text.Json;

namespace Spectralis;

public enum ExternalOpenKind
{
    File,
    Url,
    SharedPlay
}

public enum ExternalOpenIntent
{
    Default,
    PlayNow,
    QueueNext,
    QueueEnd
}

public sealed record ExternalOpenRequest(
    ExternalOpenKind Kind,
    string Value,
    string? CdnBaseUrl = null,
    ExternalOpenIntent Intent = ExternalOpenIntent.Default);

public static class ExternalOpenIpc
{
    public const string PipeName = "Spectralis.PreserveSession.v1";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
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
}
