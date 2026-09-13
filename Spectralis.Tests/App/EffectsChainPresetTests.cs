using Spectralis.App.Services;
using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

[Collection("EffectChainPresetStore")]
public sealed class EffectsChainPresetTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"effect-chain-presets-vm-{Guid.NewGuid():N}.json");

    public EffectsChainPresetTests() => EffectChainPresetStore.PathOverride = _tempFile;

    public void Dispose()
    {
        EffectChainPresetStore.PathOverride = null;
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void ChainPresetNames_StartsWithBuiltIns()
    {
        var vm = new EffectsChainViewModel(new EffectChain());

        Assert.Equal(EffectChainPresets.DefaultName, vm.ChainPresetNames[0]);
        Assert.Equal(EffectChainPresets.MakeMusicPeakName, vm.ChainPresetNames[1]);
    }

    [Fact]
    public void LoadSelectedChainPreset_Default_ReplacesRackWithOneFlatEq()
    {
        var chain = new EffectChain();
        chain.Add(new ReverbEffect());
        chain.Add(new CompressorEffect());
        var vm = new EffectsChainViewModel(chain) { SelectedChainPresetName = EffectChainPresets.DefaultName };

        vm.LoadSelectedChainPreset();

        Assert.Single(chain.Effects);
        Assert.IsType<ParametricEqEffect>(chain.Effects[0]);
        Assert.Single(vm.EffectItems);
    }

    [Fact]
    public void LoadSelectedChainPreset_MakeMusicPeak_BuildsMultiEffectChain()
    {
        var vm = new EffectsChainViewModel(new EffectChain()) { SelectedChainPresetName = EffectChainPresets.MakeMusicPeakName };

        vm.LoadSelectedChainPreset();

        Assert.True(vm.EffectItems.Count > 1);
    }

    [Fact]
    public void SaveCurrentChainAsPreset_ThenLoad_RoundTrips()
    {
        var chain = new EffectChain();
        chain.Add(new SaturationEffect());
        var vm = new EffectsChainViewModel(chain);

        vm.SaveCurrentChainAsPreset("My Rack");
        Assert.Contains("My Rack", vm.ChainPresetNames);

        chain.Add(new ReverbEffect()); // mutate the live rack
        vm.SelectedChainPresetName = "My Rack";
        vm.LoadSelectedChainPreset();

        Assert.Single(chain.Effects);
        Assert.IsType<SaturationEffect>(chain.Effects[0]);
    }

    [Fact]
    public void SavingOverABuiltInName_IsRenamed()
    {
        var vm = new EffectsChainViewModel(new EffectChain());
        vm.SaveCurrentChainAsPreset(EffectChainPresets.MakeMusicPeakName);
        Assert.Contains($"{EffectChainPresets.MakeMusicPeakName} (custom)", vm.ChainPresetNames);
    }

    [Fact]
    public void DeleteSelectedChainPreset_OnlyAllowedForUserPresets()
    {
        var vm = new EffectsChainViewModel(new EffectChain());
        vm.SaveCurrentChainAsPreset("Deletable");
        vm.SelectedChainPresetName = "Deletable";
        Assert.True(vm.CanDeleteSelectedChainPreset);

        vm.SelectedChainPresetName = EffectChainPresets.DefaultName;
        Assert.False(vm.CanDeleteSelectedChainPreset);

        vm.SelectedChainPresetName = "Deletable";
        vm.DeleteSelectedChainPreset();
        Assert.DoesNotContain("Deletable", vm.ChainPresetNames);
    }
}
