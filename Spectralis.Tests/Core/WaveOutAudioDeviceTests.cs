using System.Reflection;
using NAudio.Wave;
using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public sealed class WaveOutAudioDeviceTests
{
    [Fact]
    public void Queued_notifications_from_a_replaced_output_do_not_reach_the_engine()
    {
        using var device = new WaveOutAudioDevice();
        using var retired = new WaveOutEvent();
        using var current = new WaveOutEvent();
        // Supply uninitialized outputs so this regression never opens the user's speakers.
        // Invoke the queued handler directly, as NAudio does after a delayed Stop notification.
        typeof(WaveOutAudioDevice).GetField("_output", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(device, current);
        var notify = typeof(WaveOutAudioDevice).GetMethod("OnOutputPlaybackStopped", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<object?, StoppedEventArgs>>(device);
        var notifications = 0;
        Exception? reported = null;
        device.PlaybackStopped += (_, e) => { notifications++; reported = e.Exception; };

        notify(retired, new(null));
        notify(retired, new(new InvalidOperationException("Old output failed")));
        Assert.Equal(0, notifications);
        notify(current, new(null)); Assert.Equal(1, notifications); Assert.Null(reported);
        var failure = new InvalidOperationException("Current output failed");
        notify(current, new(failure)); Assert.Equal(2, notifications); Assert.Same(failure, reported);

        device.Dispose();
        notify(current, new(null)); Assert.Equal(2, notifications);
    }
}
