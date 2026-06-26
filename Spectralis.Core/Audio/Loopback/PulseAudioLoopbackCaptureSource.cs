using System.Diagnostics;
using Spectralis.Core.Platform;
using Spectralis.Core.Visualizers;

namespace Spectralis.Core.Audio.Loopback;

/// <summary>
/// Linux backend: records the default sink's monitor source via `parec`
/// (PulseAudio's pacat; PipeWire ships a compatible implementation, so both
/// stacks work without any virtual device). PCM s16le 44.1kHz stereo streams
/// from the child's stdout into the visualizer.
/// </summary>
public sealed class PulseAudioLoopbackCaptureSource : ILoopbackCaptureSource
{
    private const int SampleRate = 44100;
    private const int Channels = 2;

    private Process? _process;
    private Thread? _readThread;
    private VisualizerSampleProvider? _target;
    private volatile bool _stopping;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsLinux();

    public string StatusDetail { get; private set; } = "not-started";

    /// <summary>"alsa_output.x.analog-stereo" → its monitor source name.</summary>
    public static string BuildMonitorSourceName(string defaultSinkName) =>
        defaultSinkName.Trim() + ".monitor";

    public static string[] BuildDefaultSinkQueryArguments() => ["get-default-sink"];

    public static string[] BuildParecArguments(string monitorSourceName) =>
    [
        "--format=s16le",
        $"--rate={SampleRate}",
        $"--channels={Channels}",
        "--latency-msec=40",
        "-d",
        monitorSourceName,
    ];

    public bool Start(VisualizerSampleProvider target, int? targetProcessId = null)
    {
        Stop();
        _target = target;
        _stopping = false;

        string monitorSource;
        try
        {
            var sink = QueryDefaultSink();
            if (string.IsNullOrWhiteSpace(sink))
            {
                StatusDetail = "no-default-sink";
                return false;
            }

            monitorSource = BuildMonitorSourceName(sink);
        }
        catch (Exception ex)
        {
            StatusDetail = $"pactl-failed:{ex.GetType().Name}";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "parec",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var argument in BuildParecArguments(monitorSource))
            {
                psi.ArgumentList.Add(argument);
            }

            _process = Process.Start(psi)
                ?? throw new InvalidOperationException("parec did not start.");

            _readThread = new Thread(ReadLoop)
            {
                IsBackground = true,
                Name = "Spectralis pulse loopback capture",
            };
            _readThread.Start();

            StatusDetail = $"pulse-monitor:{monitorSource}";
            return true;
        }
        catch (Exception ex)
        {
            StatusDetail = $"parec-failed:{ex.GetType().Name}:{ex.Message}";
            Stop();
            return false;
        }
    }

    private static string QueryDefaultSink()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "pactl",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in BuildDefaultSinkQueryArguments())
        {
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("pactl did not start.");
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(5000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("pactl get-default-sink timed out.");
        }

        return output.Trim();
    }

    private void ReadLoop()
    {
        var process = _process;
        var visualizer = _target;
        if (process is null || visualizer is null)
        {
            return;
        }

        var byteBuffer = new byte[8192];
        var floatBuffer = new float[byteBuffer.Length / 2];

        try
        {
            var stream = process.StandardOutput.BaseStream;
            while (!_stopping)
            {
                var read = stream.Read(byteBuffer, 0, byteBuffer.Length);
                if (read <= 0)
                {
                    break;
                }

                var samples = read / 2;
                for (var i = 0; i < samples; i++)
                {
                    floatBuffer[i] = BitConverter.ToInt16(byteBuffer, i * 2) / 32768f;
                }

                visualizer.FeedExternalSamples(floatBuffer, 0, samples, Channels);
            }
        }
        catch
        {
            // Capture is best-effort; never take the app down with it.
        }
    }

    public void Stop()
    {
        _stopping = true;

        var process = _process;
        _process = null;
        if (process is not null)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }

            process.Dispose();
        }

        if (_readThread is { IsAlive: true } thread && thread != Thread.CurrentThread)
        {
            try { thread.Join(750); } catch { }
        }

        _readThread = null;
        _target = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
