using Spectralis.Core.Platform;

namespace Spectralis.Tests.Core;

/// <summary>
/// In-memory audio device: no hardware, no threads. <see cref="DrainSource"/>
/// simulates the device consuming the stream to its end.
/// </summary>
public sealed class FakeAudioDevice : IAudioDevice
{
    private IAudioSampleSource? _source;

    public string? DeviceId { get; init; }
    public int SampleRate => _source?.SampleRate ?? 0;
    public int Channels => _source?.Channels ?? 0;
    public float Volume { get; set; } = 1f;
    public bool IsPlaying { get; private set; }
    public bool IsDisposed { get; private set; }
    public int InitCount { get; private set; }

    public event EventHandler<AudioDeviceStoppedEventArgs>? PlaybackStopped;

    public void Init(IAudioSampleSource source)
    {
        _source = source;
        InitCount++;
    }

    public void Play() => IsPlaying = true;
    public void Pause() => IsPlaying = false;

    public void Stop()
    {
        if (!IsPlaying && _source is null)
        {
            return;
        }

        IsPlaying = false;
        PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(null));
    }

    /// <summary>Reads the attached source until exhausted, then raises PlaybackStopped.</summary>
    public void DrainSource()
    {
        if (_source is null)
        {
            return;
        }

        var buffer = new float[4096];
        while (_source.Read(buffer, 0, buffer.Length) > 0)
        {
        }

        IsPlaying = false;
        PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(null));
    }

    public void FailPlayback(Exception ex)
    {
        IsPlaying = false;
        PlaybackStopped?.Invoke(this, new AudioDeviceStoppedEventArgs(ex));
    }

    public void Dispose() => IsDisposed = true;
}

public sealed class FakeAudioDeviceEnumerator : IAudioDeviceEnumerator
{
    public List<FakeAudioDevice> CreatedDevices { get; } = new();

    public FakeAudioDevice? Current => CreatedDevices.Count > 0 ? CreatedDevices[^1] : null;

    public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
        new[] { new AudioDeviceInfo("fake", "Fake Device", true) };

    public IAudioDevice CreateDevice(string? deviceId, int latencyMs)
    {
        var device = new FakeAudioDevice { DeviceId = deviceId };
        CreatedDevices.Add(device);
        return device;
    }
}
