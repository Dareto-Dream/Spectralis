using Spectralis.App.Services;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class EffectChainStateTests
{
    [Fact]
    public void SerializeThenRestore_RoundTripsRackIncludingEq()
    {
        var chain = new EffectChain();
        var eq = new ParametricEqEffect();
        var bands = eq.ReadBands().ToList();
        bands[2] = bands[2] with { GainDb = 7f, Type = EqFilterType.Notch };
        eq.WriteBands(bands);
        eq.PreampDb = -4f;
        chain.Add(eq);
        chain.Add(new CompressorEffect { Enabled = false });

        var json = EffectChainState.Serialize(chain);

        var restored = new EffectChain();
        EffectChainState.Restore(restored, json);

        Assert.Equal(2, restored.Effects.Count);
        var restoredEq = Assert.IsType<ParametricEqEffect>(restored.Effects[0]);
        Assert.Equal(-4f, restoredEq.PreampDb);
        Assert.Equal(7f, restoredEq.ReadBands()[2].GainDb);
        Assert.Equal(EqFilterType.Notch, restoredEq.ReadBands()[2].Type);
        Assert.False(restored.Effects[1].Enabled);
    }

    [Fact]
    public void Restore_IgnoresBlankOrGarbageJson()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());

        EffectChainState.Restore(chain, "");
        EffectChainState.Restore(chain, "not json");

        Assert.Single(chain.Effects);
    }
}
