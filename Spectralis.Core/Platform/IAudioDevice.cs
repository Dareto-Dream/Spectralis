namespace Spectralis.Core.Platform;

/// <summary>
/// Description of an output device exposed by the platform audio backend.
/// </summary>
public sealed record AudioDeviceInfo(string Id, string FriendlyName, bool IsDefault);

/// <summary>
/// Abstraction over the platform audio output (WASAPI on Windows via NAudio today).
/// The engine renders into this; tests substitute a silent in-memory device.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>Identifier of the underlying output device, or null for system default.</summary>
    string? DeviceId { get; }

    int SampleRate { get; }
    int Channels { get; }

    float Volume { get; set; }

    bool IsPlaying { get; }

    /// <summary>Attach a sample provider (32-bit float interleaved) and prepare the device.</summary>
    void Init(IAudioSampleSource source);

    void Play();
    void Pause();
    void Stop();

    /// <summary>Raised when the device finished draining after the source ended, or the device failed.</summary>
    event EventHandler<AudioDeviceStoppedEventArgs>? PlaybackStopped;
}

public sealed class AudioDeviceStoppedEventArgs : EventArgs
{
    public AudioDeviceStoppedEventArgs(Exception? exception) => Exception = exception;
    public Exception? Exception { get; }
}

/// <summary>
/// Minimal sample source consumed by <see cref="IAudioDevice"/>. Matches NAudio's
/// ISampleProvider shape so the Windows implementation is a thin adapter.
/// </summary>
public interface IAudioSampleSource
{
    int SampleRate { get; }
    int Channels { get; }
    int Read(float[] buffer, int offset, int count);
}

/// <summary>Enumerates platform output devices.</summary>
public interface IAudioDeviceEnumerator
{
    IReadOnlyList<AudioDeviceInfo> GetOutputDevices();
    IAudioDevice CreateDevice(string? deviceId, int latencyMs);
}
