using Spectralis.App.ViewModels;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

public sealed class PanEditorViewModelTests
{
    [Fact]
    public void EffectsChain_ExposesPanEditor_ForStereoPanner_ButNotForOthers()
    {
        var chain = new EffectChain();
        chain.Add(EffectChain.CreateEffect("Stereo Panner"));
        chain.Add(EffectChain.CreateEffect("Reverb"));
        var vm = new EffectsChainViewModel(chain);

        Assert.True(vm.EffectItems[0].HasPanEditor);
        Assert.NotNull(vm.EffectItems[0].PanEditor);
        Assert.False(vm.EffectItems[1].HasPanEditor);

        vm.SelectedEffect = vm.EffectItems[0];
        Assert.True(vm.IsPanSelected);
        Assert.True(vm.IsWideEditorSelected);
        vm.SelectedEffect = vm.EffectItems[1];
        Assert.False(vm.IsPanSelected);
        Assert.False(vm.IsWideEditorSelected);
    }

    [Fact]
    public void TogglingAutoPan_UpdatesEffect_AndNotifiesWithoutRequiringChainRebuild()
    {
        var effect = new StereoPannerEffect();
        var edited = 0;
        var vm = new PanEditorViewModel(effect, () => edited++);

        Assert.False(vm.AutoPanEnabled);
        vm.AutoPanEnabled = true;

        Assert.True(effect.AutoPanEnabled);
        Assert.True(edited > 0);
    }

    [Fact]
    public void BarsAndBpm_ClampToValidRange()
    {
        var effect = new StereoPannerEffect();
        var vm = new PanEditorViewModel(effect, () => { });

        vm.Bars = 999;
        vm.Bpm = 5;

        Assert.Equal(16, vm.Bars);
        Assert.Equal(40, vm.Bpm);
    }

    [Fact]
    public void AddAndRemovePoint_RespectLimits()
    {
        var effect = new StereoPannerEffect();
        var vm = new PanEditorViewModel(effect, () => { });
        var start = vm.Points.Count;

        vm.AddPointAt(0.6, 0.3);
        Assert.Equal(start + 1, vm.Points.Count);

        vm.RemovePoint(vm.Points[^1]);
        Assert.Equal(start, vm.Points.Count);

        while (vm.CanRemovePoint)
        {
            vm.RemovePoint(vm.Points[^1]);
        }

        Assert.Equal(StereoPannerEffect.MinPoints, vm.Points.Count);
    }

    [Fact]
    public void DraggingAPoint_WritesThroughToTheEffect()
    {
        var effect = new StereoPannerEffect();
        var vm = new PanEditorViewModel(effect, () => { });

        vm.Points[1].Time = 0.4;
        vm.Points[1].Pan = -0.5;

        var stored = effect.ReadPoints()[1];
        Assert.Equal(0.4f, stored.Time, 3);
        Assert.Equal(-0.5f, stored.Pan, 3);
    }

    [Fact]
    public void ResetToDefault_RestoresDefaultLoop()
    {
        var effect = new StereoPannerEffect();
        var vm = new PanEditorViewModel(effect, () => { });
        vm.AddPointAt(0.9, -0.9);

        vm.ResetToDefault();

        Assert.Equal(StereoPannerEffect.DefaultPoints.Length, vm.Points.Count);
        for (var i = 0; i < vm.Points.Count; i++)
        {
            Assert.Equal(StereoPannerEffect.DefaultPoints[i].Pan, vm.Points[i].Pan, 3);
        }
    }

    [Fact]
    public void ComputeCurve_MatchesEvaluatePan()
    {
        var effect = new StereoPannerEffect();
        var vm = new PanEditorViewModel(effect, () => { });

        var curve = vm.ComputeCurve(5);

        Assert.Equal(5, curve.Length);
        Assert.Equal(StereoPannerEffect.EvaluatePan(StereoPannerEffect.DefaultPoints, 0f), curve[0], 3);
    }
}
