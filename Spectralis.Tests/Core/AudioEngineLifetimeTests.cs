using NAudio.Wave;
using Spectralis.Core.Audio;
using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AudioEngineLifetimeTests
{
    [Theory]
    [InlineData("load")]
    [InlineData("seamless")]
    [InlineData("unload")]
    [InlineData("dispose")]
    public void Delayed_stop_during_decoder_disposal_does_not_read_the_retired_decoder(string action)
    {
        var devices = new DeferredDevices();
        var streams = new List<DisposalSensitiveStream>();
        using var engine = new AudioEngine(devices, _ => { var s = new DisposalSensitiveStream(); streams.Add(s); return s; });
        engine.Load("first.ogg"); engine.Play();
        var ended = 0; engine.TrackEnded += (_, _) => ended++;
        // Already queued notifications can outlive event unsubscription and device disposal.
        streams[0].AfterDispose = devices.Current!.CaptureStopped();
        switch (action)
        {
            case "load": engine.Load("second.ogg"); break;
            case "seamless": Assert.True(engine.TrySeamlessAdvance("second.ogg")); break;
            case "unload": engine.Unload(); break;
            case "dispose": engine.Dispose(); break;
        }
        Assert.True(streams[0].Disposed); Assert.Equal(0, ended);
        if (action is "load" or "seamless")
        {
            Assert.True(engine.IsLoaded); Assert.Equal("second.ogg", engine.CurrentTrack!.SourcePath);
            Assert.True(engine.GetLength() > 0);
        }
        else Assert.False(engine.IsLoaded);
    }

    [Fact]
    public void Failed_seamless_init_clears_the_disposed_stream_before_the_normal_load_fallback()
    {
        var devices = new DeferredDevices();
        using var engine = new AudioEngine(devices, _ => new DisposalSensitiveStream());
        engine.Load("first.ogg"); engine.Play();
        var pendingStop = devices.Current!.CaptureStopped();
        devices.Current.FailNextInit = true;
        Assert.False(engine.TrySeamlessAdvance("second.ogg"));
        Assert.False(engine.IsLoaded); Assert.Null(engine.CurrentTrack);
        Assert.Equal(0, engine.GetLength()); Assert.Equal(0, engine.GetPosition());
        pendingStop();
        engine.Load("second.ogg"); engine.Play();
        Assert.True(engine.IsPlaying); Assert.Equal("second.ogg", engine.CurrentTrack!.SourcePath);
    }

    [Fact]
    public void Stop_callback_already_in_flight_when_unload_wins_does_not_crash_or_advance()
    {
        var devices = new DeferredDevices(); var stream = new DisposalSensitiveStream();
        using var engine = new AudioEngine(devices, _ => stream);
        engine.Load("first.ogg"); engine.Play();
        var ended = 0; engine.TrackEnded += (_, _) => ended++;
        stream.BeforeLength = engine.Unload;
        devices.Current!.Finish();
        Assert.True(stream.Disposed); Assert.False(engine.IsLoaded); Assert.Equal(0, ended);
    }

    [Fact]
    public void Output_rebuild_ignores_notifications_from_the_retired_device()
    {
        var devices = new DeferredDevices();
        using var engine = new AudioEngine(devices, _ => new DisposalSensitiveStream());
        engine.Load("first.ogg"); engine.Play();
        var oldDevice = devices.Current!;
        oldDevice.AfterDispose = oldDevice.CaptureStopped();
        var ended = 0; engine.TrackEnded += (_, _) => ended++;
        engine.SetPreferredSampleRate(48000);
        oldDevice.AfterDispose();
        Assert.NotSame(oldDevice, devices.Current); Assert.True(engine.IsPlaying); Assert.Equal(0, ended);
    }

    // Vorbis throws from Length after Dispose; WAV readers often retain their length,
    // which is why ordinary WAV-only lifecycle tests missed both recorded crashes.
    private sealed class DisposalSensitiveStream : WaveStream
    {
        public bool Disposed { get; private set; }
        public Action? AfterDispose { get; set; }
        public Action? BeforeLength { get; set; }
        public override WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(8000, 2);
        public override long Length
        {
            get
            {
                var hook = BeforeLength; BeforeLength = null; hook?.Invoke();
                ObjectDisposedException.ThrowIf(Disposed, this); return 64000;
            }
        }
        private long _position;
        public override long Position { get { ObjectDisposedException.ThrowIf(Disposed, this); return _position; } set => _position = value; }
        public override int Read(byte[] buffer, int offset, int count)
        { var read = (int)Math.Min(count, Length - Position); Array.Clear(buffer, offset, read); Position += read; return read; }
        protected override void Dispose(bool disposing)
        { if (Disposed) return; Disposed = true; AfterDispose?.Invoke(); base.Dispose(disposing); }
    }

    private sealed class DeferredDevices : IAudioDeviceEnumerator
    {
        public DeferredDevice? Current { get; private set; }
        public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => [];
        public IAudioDevice CreateDevice(string? deviceId, int latencyMs) => Current = new DeferredDevice();
    }
    private sealed class DeferredDevice : IAudioDevice
    {
        public string? DeviceId => null;
        public int SampleRate => 8000;
        public int Channels => 2;
        public float Volume { get; set; }
        public bool IsPlaying { get; private set; }
        public bool FailNextInit { get; set; }
        public Action? AfterDispose { get; set; }
        public event EventHandler<AudioDeviceStoppedEventArgs>? PlaybackStopped;
        public void Init(IAudioSampleSource source)
        { if (FailNextInit) { FailNextInit = false; throw new InvalidOperationException("Output init failed"); } }
        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Stop() => IsPlaying = false;
        public Action CaptureStopped() { var callback = PlaybackStopped; return () => callback?.Invoke(this, new(null)); }
        public void Finish() { IsPlaying = false; CaptureStopped()(); }
        public void Dispose() { IsPlaying = false; AfterDispose?.Invoke(); }
    }
}
