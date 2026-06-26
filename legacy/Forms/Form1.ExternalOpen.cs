using System.IO;
using System.IO.Pipes;
using System.Text;

namespace Spectralis;

public partial class Form1
{
    private readonly CancellationTokenSource externalOpenIpcCancellation = new();

    private void StartExternalOpenIpcServer()
    {
        _ = Task.Run(ListenForExternalOpenRequestsAsync);
    }

    private async Task ListenForExternalOpenRequestsAsync()
    {
        var cancellationToken = externalOpenIpcCancellation.Token;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    ExternalOpenIpc.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipe.WaitForConnectionAsync(cancellationToken);
                var request = await ExternalOpenIpc.ReadRequestAsync(pipe, cancellationToken);
                if (request is not null)
                    BeginInvoke(() => _ = HandleExternalOpenRequestAsync(request));

                await WriteExternalOpenResponseAsync(pipe, "OK", cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                await Task.Delay(250, cancellationToken).ContinueWith(static _ => { });
            }
        }
    }

    private static async Task WriteExternalOpenResponseAsync(
        Stream pipe,
        string response,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(response + Environment.NewLine);
        await pipe.WriteAsync(bytes, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private async Task HandleExternalOpenRequestAsync(ExternalOpenRequest request)
    {
        if (IsDisposed)
            return;

        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;
        Activate();

        try
        {
            switch (request.Kind)
            {
                case ExternalOpenKind.File:
                    await HandleExternalFileOpenAsync(request.Value, request.Intent);
                    break;

                case ExternalOpenKind.Url:
                    await HandleExternalUrlOpenAsync(request.Value, request.Intent);
                    break;

                case ExternalOpenKind.SharedPlay:
                    await JoinSharedPlaySessionAsync(new SharedPlayJoinRequest(request.Value, request.CdnBaseUrl));
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowError(
                $"Spectralis could not open the requested item.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open Error");
        }
    }

    private Task HandleExternalFileOpenAsync(
        string path,
        ExternalOpenIntent intent = ExternalOpenIntent.Default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Task.CompletedTask;

        if (IsAlbumCapsulePath(path))
            return OpenAlbumCapsuleAsync(path);

        if (IsCapsulePath(path))
            return OpenCapsuleAsync(path);

        if (intent is ExternalOpenIntent.QueueNext or ExternalOpenIntent.QueueEnd)
        {
            var insertMode = intent == ExternalOpenIntent.QueueEnd
                ? QueueInsertMode.End
                : QueueInsertMode.Next;

            QueueLocalFiles([path], playIfQueueWasEmpty: appSettings.AutoPlayOnOpen, insertMode);
        }
        else if (intent == ExternalOpenIntent.PlayNow)
        {
            LoadFilesAsQueue([path], startPlayback: true);
        }
        else if (appSettings.QueueByDefault)
        {
            if (IsSpotifyActive)
                StartLocalInterludeFromSpotify([path]);
            else
                QueueLocalFiles([path], playIfQueueWasEmpty: appSettings.AutoPlayOnOpen);
        }
        else
        {
            LoadFilesAsQueue([path], appSettings.AutoPlayOnOpen);
        }

        return Task.CompletedTask;
    }

    private async Task HandleExternalUrlOpenAsync(
        string url,
        ExternalOpenIntent intent = ExternalOpenIntent.Default)
    {
        if (intent is ExternalOpenIntent.QueueNext or ExternalOpenIntent.QueueEnd)
        {
            await QueueExternalUrlAsync(
                url,
                intent == ExternalOpenIntent.QueueEnd ? QueueInsertMode.End : QueueInsertMode.Next);
            return;
        }

        if (intent == ExternalOpenIntent.PlayNow)
        {
            await OpenDetectedUrlAsync(url);
            return;
        }

        if (appSettings.QueueByDefault &&
            await TryQueueExternalDirectAudioUrlAsync(url))
        {
            return;
        }

        await OpenDetectedUrlAsync(url);
    }

    private async Task QueueExternalUrlAsync(string url, QueueInsertMode insertMode)
    {
        var expandedUrl = await TryExpandOpenUrlAsync(url.Trim(), CancellationToken.None);
        if (expandedUrl is null)
        {
            ShowError(
                "Paste a supported URL: YouTube, SoundCloud, Suno, Spotify, Untitled, BandLab, a Suno clip ID, or a direct audio file link.",
                "Open URL");
            return;
        }

        QueueExternalPointers(
            [expandedUrl.Url],
            playIfQueueWasEmpty: appSettings.AutoPlayOnOpen,
            insertMode);
    }

    private async Task<bool> TryQueueExternalDirectAudioUrlAsync(string url)
    {
        var expandedUrl = await TryExpandOpenUrlAsync(url.Trim(), CancellationToken.None);
        if (expandedUrl?.Target is not OpenUrlTarget.DirectAudio ||
            !Uri.TryCreate(expandedUrl.Url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var extension = Path.GetExtension(uri.AbsolutePath);
        var cachedPath = await RemoteAudioCache.DownloadAsync(uri.AbsoluteUri, extension, CancellationToken.None);
        QueueLocalFiles([cachedPath], playIfQueueWasEmpty: appSettings.AutoPlayOnOpen);
        return true;
    }

    public void StopExternalOpenIpc()
    {
        externalOpenIpcCancellation.Cancel();
    }

    private void DisposeExternalOpenIpc()
    {
        externalOpenIpcCancellation.Cancel();
        externalOpenIpcCancellation.Dispose();
    }
}
