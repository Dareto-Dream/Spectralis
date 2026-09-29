using Spectralis.App.Services;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class WorldDspPresetControllerTests
{
    [Fact]
    public void ApplyWorldPreset_ReplacesRackAndMarksWorldManaged()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        var controller = new WorldDspPresetController(chain);

        var presetChain = new EffectChain();
        presetChain.Add(new CompressorEffect());
        var presetJson = EffectChainState.Serialize(presetChain);

        controller.ApplyWorldPreset(presetJson);

        Assert.True(controller.IsActive);
        Assert.True(chain.IsWorldManaged);
        Assert.Single(chain.Effects);
        Assert.IsType<CompressorEffect>(chain.Effects[0]);
    }

    [Fact]
    public void RevertToUser_RestoresOriginalRackAndClearsFlag()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect { Enabled = false });
        var controller = new WorldDspPresetController(chain);

        var presetChain = new EffectChain();
        presetChain.Add(new CompressorEffect());
        controller.ApplyWorldPreset(EffectChainState.Serialize(presetChain));

        controller.RevertToUser();

        Assert.False(controller.IsActive);
        Assert.False(chain.IsWorldManaged);
        Assert.Single(chain.Effects);
        var reverb = Assert.IsType<ReverbEffect>(chain.Effects[0]);
        Assert.False(reverb.Enabled);
    }

    [Fact]
    public void ApplyWorldPreset_TwiceInARow_DoesNotSnapshotThePreviousWorldPreset()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        var controller = new WorldDspPresetController(chain);

        var presetA = new EffectChain();
        presetA.Add(new CompressorEffect());
        controller.ApplyWorldPreset(EffectChainState.Serialize(presetA));

        var presetB = new EffectChain();
        presetB.Add(new DelayEffect());
        controller.ApplyWorldPreset(EffectChainState.Serialize(presetB));

        Assert.Single(chain.Effects);
        Assert.IsType<DelayEffect>(chain.Effects[0]);

        controller.RevertToUser();

        // Reverting after the second apply should still bring back the *user's*
        // original rack (Reverb), not preset A.
        Assert.Single(chain.Effects);
        Assert.IsType<ReverbEffect>(chain.Effects[0]);
    }

    [Fact]
    public void RevertToUser_WithoutAnActivePreset_IsANoOp()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        var controller = new WorldDspPresetController(chain);

        controller.RevertToUser();

        Assert.False(chain.IsWorldManaged);
        Assert.Single(chain.Effects);
    }

    [Fact]
    public void ApplyWorldPreset_UnknownEffectName_SkippedNotThrown()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        var controller = new WorldDspPresetController(chain);

        var presetJson = """{"Enabled":true,"Effects":[{"Name":"Not A Real Effect","Enabled":true,"Params":{}},{"Name":"Delay","Enabled":true,"Params":{}}]}""";

        controller.ApplyWorldPreset(presetJson);

        Assert.Single(chain.Effects);
        Assert.IsType<DelayEffect>(chain.Effects[0]);
    }

    [Fact]
    public void ApplyWorldPreset_BlankJson_IsIgnored()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        var controller = new WorldDspPresetController(chain);

        controller.ApplyWorldPreset("");

        Assert.False(controller.IsActive);
        Assert.Single(chain.Effects);
    }
}
