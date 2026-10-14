using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Spectralis.Core.Audio;
using Spectralis.Core.Integrations;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>
/// Guards the decoder that replaced MediaFoundationReader off Windows. The bug:
/// yt-dlp hands back m4a/webm for YouTube, nothing had a direct NAudio reader for
/// those, and the MediaFoundation fallback is Windows-only — so tracks resolved and
/// downloaded fine, then failed the moment playback tried to open them.
/// </summary>
public class FfmpegWaveStreamTests : IDisposable
{
    private readonly string _workDir;

    public FfmpegWaveStreamTests()
    {
        _workDir = Path.Combine(Path.GetTempPath(), "spectralis-decode-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workDir, recursive: true); } catch { }
    }

    [Theory]
    [InlineData("m4a")]   // yt-dlp's first choice for YouTube
    [InlineData("webm")]  // its second choice
    public void Open_DecodesFormatsWithNoDirectReader(string container)
    {
        var ffmpeg = FfmpegLocator.FindExecutable();
        Assert.NotNull(ffmpeg);

        var encoded = Encode(ffmpeg!, container, seconds: 2);

        using var stream = FfmpegWaveStream.Open(encoded);

        Assert.Equal(2, stream.WaveFormat.Channels);
        Assert.True(stream.Length > 0, "decoded stream is empty");
        Assert.InRange(stream.TotalTime.TotalSeconds, 1.5, 2.5);

        // Read it through and confirm it isn't just silence.
        var buffer = new byte[8192];
        var total = 0L;
        var nonZero = false;
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (!nonZero)
            {
                for (var i = 0; i < read; i++)
                {
                    if (buffer[i] != 0) { nonZero = true; break; }
                }
            }
        }

        Assert.Equal(stream.Length, total);
        Assert.True(nonZero, "decoded audio was entirely silent");
    }

    [Fact]
    public void Open_IsSeekableAndCleansUpItsTempFile()
    {
        var ffmpeg = FfmpegLocator.FindExecutable();
        Assert.NotNull(ffmpeg);

        var encoded = Encode(ffmpeg!, "m4a", seconds: 2);

        using var stream = FfmpegWaveStream.Open(encoded);
        var decodedPath = stream.DecodedPath;
        Assert.True(File.Exists(decodedPath));
        stream.Position = stream.Length / 2;
        Assert.Equal(stream.Length / 2, stream.Position);
        Assert.True(stream.Read(new byte[1024], 0, 1024) > 0, "could not read after seeking");
        stream.Dispose();

        Assert.False(File.Exists(decodedPath), "Dispose left its temporary WAV behind");
    }

    [Fact]
    public void Open_UndecodableInput_Throws()
    {
        var junk = Path.Combine(_workDir, "not-audio.m4a");
        File.WriteAllText(junk, "this is not audio");

        Assert.ThrowsAny<Exception>(() => FfmpegWaveStream.Open(junk));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Open_HungInput_RespectsTimeoutAndCancellation(bool cancel)
    {
        // Accept FFmpeg's HTTP request without replying, so stderr stays open and
        // silent. Reading stderr synchronously before waiting used to hang forever.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var cancellation = new CancellationTokenSource();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var decode = FfmpegWaveStream.OpenAsync($"http://127.0.0.1:{port}/hung.m4a",
            timeout: TimeSpan.FromSeconds(cancel ? 30 : 2), cancellationToken: cancellation.Token);
        using var client = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(10));

        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decode.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        else
        {
            await Assert.ThrowsAsync<TimeoutException>(() => decode.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(decode.IsCompleted, "Only the test deadline expired; FFmpeg is still blocked.");
        }
    }

    /// <summary>Renders a short stereo tone into the requested container.</summary>
    private string Encode(string ffmpeg, string container, int seconds)
    {
        var output = Path.Combine(_workDir, $"tone.{container}");
        var codec = container == "webm" ? "libopus" : "aac";

        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
                 {
                     "-hide_banner", "-loglevel", "error", "-nostdin",
                     "-f", "lavfi", "-i", $"sine=frequency=440:duration={seconds}:sample_rate=44100",
                     "-ac", "2", "-c:a", codec, "-y", output,
                 })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(60_000);

        Assert.True(File.Exists(output), $"ffmpeg failed to produce {container}: {stderr}");
        return output;
    }
}
