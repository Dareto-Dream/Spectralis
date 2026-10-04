using Spectralis.Core.Audio;
using Spectralis.Core.Platform;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class AudioOutputManagerTests : IDisposable
{
    /// <summary>Enumerator whose device list the test can change, like hot-plugging hardware.</summary>
    private sealed class PluggableEnumerator : IAudioDeviceEnumerator
    {
        public List<AudioDeviceInfo> Devices { get; } = [new("-1", "System default", true)];
        public List<FakeAudioDevice> Created { get; } = [];

        public void Plug(string name) => Devices.Add(new AudioDeviceInfo(name, name, false));
        public void Unplug(string name) => Devices.RemoveAll(d => d.Id == name);

        public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() => Devices.ToList();

        public IAudioDevice CreateDevice(string? deviceId, int latencyMs)
        {
            var device = new FakeAudioDevice { DeviceId = deviceId };
            Created.Add(device);
            return device;
        }
    }

    private readonly PluggableEnumerator _devices = new();
    private readonly AudioEngine _engine;
    private readonly string _wav;

    public AudioOutputManagerTests()
    {
        _engine = new AudioEngine(_devices);
        _wav = WavFixture.CreateSineWav(1.0, 8000, 2);
        _engine.Load(_wav);
        _engine.Play();
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { File.Delete(_wav); } catch { }
    }

    [Fact]
    public void Picking_a_present_device_moves_the_engine_onto_it()
    {
        _devices.Plug("USB DAC");
        using var manager = new AudioOutputManager(_engine, _devices);

        manager.SetPreferred("USB DAC");

        Assert.Equal("USB DAC", manager.ActiveDeviceId);
        Assert.Equal("USB DAC", _devices.Created[^1].DeviceId);
        Assert.True(_engine.IsPlaying);
        Assert.False(manager.IsFallbackActive);
    }

    [Fact]
    public void Unplugging_falls_back_to_default_and_replugging_restores()
    {
        _devices.Plug("USB DAC");
        using var manager = new AudioOutputManager(_engine, _devices, "USB DAC");
        var notices = new List<AudioOutputNotice>();
        manager.Notice += notices.Add;

        _devices.Unplug("USB DAC");
        manager.Refresh();

        Assert.Null(manager.ActiveDeviceId);
        Assert.Equal("USB DAC", manager.PreferredDeviceId); // still remembered
        Assert.True(manager.IsFallbackActive);
        Assert.Null(_devices.Created[^1].DeviceId);
        Assert.Equal([new AudioOutputNotice(AudioOutputNoticeKind.FellBackToDefault, "USB DAC")], notices);

        _devices.Plug("USB DAC");
        manager.Refresh();

        Assert.Equal("USB DAC", manager.ActiveDeviceId);
        Assert.Equal("USB DAC", _devices.Created[^1].DeviceId);
        Assert.False(manager.IsFallbackActive);
        Assert.Equal(AudioOutputNoticeKind.Restored, notices[^1].Kind);
    }

    [Fact]
    public void A_preferred_device_missing_at_startup_starts_on_default_and_waits()
    {
        using var manager = new AudioOutputManager(_engine, _devices, "Headset");

        Assert.Null(manager.ActiveDeviceId);
        Assert.True(manager.IsFallbackActive);

        _devices.Plug("Headset");
        manager.Refresh();

        Assert.Equal("Headset", manager.ActiveDeviceId);
    }

    [Fact]
    public void Unchanged_device_list_does_not_rebuild_the_output_or_raise_events()
    {
        _devices.Plug("USB DAC");
        using var manager = new AudioOutputManager(_engine, _devices);
        manager.SetPreferred("USB DAC");
        var created = _devices.Created.Count;
        var changed = 0;
        manager.DevicesChanged += () => changed++;

        manager.Refresh();
        manager.Refresh();

        Assert.Equal(created, _devices.Created.Count);
        Assert.Equal(0, changed);
    }

    [Fact]
    public void Plugging_an_unrelated_device_raises_DevicesChanged_but_keeps_playing_on_the_same_device()
    {
        using var manager = new AudioOutputManager(_engine, _devices);
        var created = _devices.Created.Count;
        var changed = 0;
        manager.DevicesChanged += () => changed++;

        _devices.Plug("Bluetooth speaker");
        manager.Refresh();

        Assert.Equal(1, changed);
        Assert.Equal(created, _devices.Created.Count);
        Assert.Contains(manager.Devices, d => d.Id == "Bluetooth speaker");
    }

    [Fact]
    public void Default_ids_are_all_treated_as_the_system_default()
    {
        _devices.Plug("USB DAC");
        using var manager = new AudioOutputManager(_engine, _devices, "USB DAC");

        manager.SetPreferred("-1");

        Assert.Null(manager.PreferredDeviceId);
        Assert.Null(manager.ActiveDeviceId);
        Assert.False(manager.IsFallbackActive);
    }

    [Fact]
    public void A_failing_enumeration_keeps_the_current_device()
    {
        var flaky = new FlakyEnumerator(_devices);
        _devices.Plug("USB DAC");
        using var manager = new AudioOutputManager(_engine, flaky, "USB DAC");

        flaky.Fail = true;
        manager.Refresh();

        Assert.Equal("USB DAC", manager.ActiveDeviceId);
    }

    private sealed class FlakyEnumerator(PluggableEnumerator inner) : IAudioDeviceEnumerator
    {
        public bool Fail { get; set; }

        public IReadOnlyList<AudioDeviceInfo> GetOutputDevices() =>
            Fail ? throw new InvalidOperationException("device service restarting") : inner.GetOutputDevices();

        public IAudioDevice CreateDevice(string? deviceId, int latencyMs) => inner.CreateDevice(deviceId, latencyMs);
    }

    [Theory]
    [InlineData(null, -1)]
    [InlineData("-1", -1)]
    [InlineData("Speakers", 0)]
    [InlineData("Headset", 1)]
    [InlineData("Headset #2", 2)]
    [InlineData("Gone", -1)]
    public void WaveOut_ids_resolve_by_name_and_duplicates_get_suffixes(string? id, int expectedNumber)
    {
        string[] names = ["Speakers", "Headset", "Headset"];

        Assert.Equal(expectedNumber, WaveOutDeviceNaming.ResolveNumber(id, names));
        Assert.Equal(["-1", "Speakers", "Headset", "Headset #2"],
            WaveOutDeviceNaming.Build(names).Select(d => d.Id));
    }
}
