using Spectralis.Core.Audio;
using Xunit;

namespace Spectralis.Tests.Core;

public class PcmWritePacerTests
{
    [Fact]
    public void ReadsFollowAudioTimeInsteadOfPipeCapacity()
    {
        var pacer = new PcmWritePacer(48000, 2);
        Assert.Equal(TimeSpan.Zero, pacer.DelayBeforeRead(TimeSpan.Zero));
        pacer.RecordWrite(960, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMilliseconds(10), pacer.DelayBeforeRead(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(5), pacer.DelayBeforeRead(TimeSpan.FromMilliseconds(5)));
        Assert.Equal(TimeSpan.Zero, pacer.DelayBeforeRead(TimeSpan.FromMilliseconds(10)));
    }

    [Fact]
    public void SlowWritesAndPausesDoNotCreateCatchUpBursts()
    {
        var pacer = new PcmWritePacer(48000, 2);
        pacer.RecordWrite(960, TimeSpan.FromSeconds(1));
        pacer.RecordWrite(960, TimeSpan.FromSeconds(1));
        Assert.InRange(pacer.DelayBeforeRead(TimeSpan.FromSeconds(1)).TotalMilliseconds, 9.9, 10.1);
        pacer.Reset(TimeSpan.FromSeconds(10));
        pacer.RecordWrite(960, TimeSpan.FromSeconds(10));
        Assert.InRange(pacer.DelayBeforeRead(TimeSpan.FromSeconds(10)).TotalMilliseconds, 9.9, 10.1);
    }
}
