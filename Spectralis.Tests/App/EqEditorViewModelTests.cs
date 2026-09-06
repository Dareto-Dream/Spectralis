using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("EqPresetStore")]
public sealed class EqEditorViewModelTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"eq-presets-vm-{Guid.NewGuid():N}.json");

    public EqEditorViewModelTests() => EqPresetStore.PathOverride = _tempFile;

    public void Dispose()
    {
        EqPresetStore.PathOverride = null;
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void EffectsChain_ExposesEqEditor_ForParametricEq_ButNotForOthers()
    {
        var chain = new EffectChain();
        chain.Add(EffectChain.CreateEffect("Parametric EQ"));
        chain.Add(EffectChain.CreateEffect("Reverb"));
        var vm = new EffectsChainViewModel(chain);

        Assert.True(vm.EffectItems[0].HasEqEditor);
        Assert.NotNull(vm.EffectItems[0].EqEditor);
        Assert.False(vm.EffectItems[1].HasEqEditor);

        vm.SelectedEffect = vm.EffectItems[0];
        Assert.True(vm.IsEqSelected);
        vm.SelectedEffect = vm.EffectItems[1];
        Assert.False(vm.IsEqSelected);
    }

    [Fact]
    public void ApplyingPreset_UpdatesBands_AndComputesResponseCurve()
    {
        var eq = new ParametricEqEffect();
        var edited = 0;
        var vm = new EqEditorViewModel(eq, () => edited++);

        vm.ApplyPreset("Bass Boost");

        Assert.Equal("Bass Boost", vm.SelectedPresetName);
        Assert.Contains(vm.Bands, b => b.Frequency <= 63 && b.GainDb > 3);
        Assert.True(edited > 0);

        var curve = vm.ComputeResponseCurve(64, 20, 20000);
        Assert.Equal(64, curve.Length);
        Assert.True(curve[0] > 1.0, "bass boost should lift the low end of the curve");
    }

    [Fact]
    public void AddAndRemoveBand_RespectLimits_AndMarkCurvePresetCustom()
    {
        var eq = new ParametricEqEffect();
        var vm = new EqEditorViewModel(eq, () => { });
        var start = vm.Bands.Count;

        vm.AddBandAt(1234, 5);
        Assert.Equal(start + 1, vm.Bands.Count);
        Assert.Equal(EqEditorViewModel.CustomLabel, vm.SelectedPresetName);

        vm.RemoveBand(vm.Bands[^1]);
        Assert.Equal(start, vm.Bands.Count);

        while (vm.CanRemoveBand)
        {
            vm.RemoveBand(vm.Bands[^1]);
        }

        Assert.Equal(ParametricEqEffect.MinBands, vm.Bands.Count);
    }

    [Fact]
    public void SaveAndDeletePreset_RoundTripsThroughStore()
    {
        var eq = new ParametricEqEffect();
        var vm = new EqEditorViewModel(eq, () => { });
        vm.Bands[0].GainDb = 6;

        vm.SaveCurrentAsPreset("Mine");
        Assert.Contains("Mine", vm.PresetNames);
        Assert.Contains(EqPresetStore.Load(), p => p.Name == "Mine");

        vm.SelectedPresetName = "Mine";
        Assert.True(vm.CanDeleteSelectedPreset);
        vm.DeleteSelectedPreset();
        Assert.DoesNotContain("Mine", vm.PresetNames);
    }

    [Fact]
    public void SavingOverABuiltInName_IsRenamed()
    {
        var vm = new EqEditorViewModel(new ParametricEqEffect(), () => { });
        vm.SaveCurrentAsPreset("Rock");
        Assert.Contains("Rock (custom)", vm.PresetNames);
    }
}
