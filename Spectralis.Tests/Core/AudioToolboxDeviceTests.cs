using Spectralis.Core.Audio;
using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.Core;

/// <summary>
/// Covers the macOS output leg. The regression these guard against: the factory
/// used to be a Linux/else split, so macOS was handed the winmm-backed WaveOut
/// device and threw "Unable to load shared library 'winmm.dll'" on first play.
/// </summary>
public class AudioDeviceEnumeratorFactoryTests
{
    [Fact]
    public void Factory_ReturnsPlatformBackend()
    {
        var enumerator = AudioDeviceEnumeratorFactory.Create();

        if (OperatingSystem.IsLinux())
        {
            Assert.IsType<PulseAudioDeviceEnumerator>(enumerator);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Assert.IsType<AudioToolboxDeviceEnumerator>(enumerator);
        }
        else
        {
            Assert.IsType<WaveOutDeviceEnumerator>(enumerator);
        }
    }
}

public class AudioToolboxDeviceTests
{
    /// <summary>
    /// Drives the real pipeline: locates the bundled ffmpeg, opens its audiotoolbox
    /// output and streams PCM into it. Asserting the source got drained is what
    /// proves the whole hop works — a Linux ELF ffmpeg (the bug this replaced)
    /// fails to exec and reads nothing.
    /// </summary>
    [Fact]
    public void Play_StreamsSamplesThroughBundledFfmpeg()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // Under `dotnet test` Environment.ProcessPath is the dotnet muxer, not this
        // directory, so the device's app-dir probe can't see the bundled binary that
        // the build copied here. Put it on PATH and its PATH fallback picks it up.
        using var _ = WithBundledFfmpegOnPath();

        var source = new SineSource(sampleRate: 44100, channels: 2, seconds: 0.5);
        using var device = AudioDeviceEnumeratorFactory.Create().CreateDevice("default", latencyMs: 50);

        Exception? failure = null;
        var finished = new ManualResetEventSlim(false);
        device.PlaybackStopped += (_, e) =>
        {
            failure = e.Exception;
            finished.Set();
        };

        device.Init(source);
        device.Play();

        finished.Wait(TimeSpan.FromSeconds(5));
        device.Stop();

        Assert.Null(failure);
        Assert.True(
            source.TotalSamplesRead > 0,
            "AudioQueue never pulled samples — the interop or stream format is wrong.");
    }

    [Fact]
    public void StopBeforePlay_AndDoubleDispose_AreSafe()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var device = AudioDeviceEnumeratorFactory.Create().CreateDevice("default", latencyMs: 50);

        device.Stop();
        device.Init(new SineSource(44100, 2, 0.05));
        device.Stop();
        device.Dispose();
        device.Dispose();
    }

    /// <summary>Prepends the test output directory (where the build drops the bundled
    /// ffmpeg) to PATH for the duration of a test.</summary>
    private static IDisposable WithBundledFfmpegOnPath()
    {
        var previous = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable(
            "PATH", AppContext.BaseDirectory + Path.PathSeparator + previous);
        return new Restore(() => Environment.SetEnvironmentVariable("PATH", previous));
    }

    private sealed class Restore : IDisposable
    {
        private readonly Action _onDispose;
        public Restore(Action onDispose) => _onDispose = onDispose;
        public void Dispose() => _onDispose();
    }

    /// <summary>Finite 440Hz tone; reports how much the device actually consumed.</summary>
    private sealed class SineSource : IAudioSampleSource
    {
        private readonly int _totalFrames;
        private int _frame;

        public SineSource(int sampleRate, int channels, double seconds)
        {
            SampleRate = sampleRate;
            Channels = channels;
            _totalFrames = (int)(sampleRate * seconds);
        }

        public int SampleRate { get; }
        public int Channels { get; }
        public int TotalSamplesRead { get; private set; }

        public int Read(float[] buffer, int offset, int count)
        {
            var framesWanted = count / Channels;
            var frames = Math.Min(framesWanted, _totalFrames - _frame);
            if (frames <= 0)
            {
                return 0;
            }

            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(Math.Sin(2 * Math.PI * 440 * (_frame + i) / SampleRate) * 0.2);
                for (var c = 0; c < Channels; c++)
                {
                    buffer[offset + (i * Channels) + c] = sample;
                }
            }

            _frame += frames;
            var written = frames * Channels;
            TotalSamplesRead += written;
            return written;
        }
    }
}
