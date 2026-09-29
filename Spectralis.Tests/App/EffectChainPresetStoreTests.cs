using Spectralis.App.Services;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

/// <summary>
/// <see cref="EffectChainPresetStore.PathOverride"/> is process-global, so the
/// tests that set it must not run in parallel with each other.
/// </summary>
[CollectionDefinition("EffectChainPresetStore", DisableParallelization = true)]
public sealed class EffectChainPresetStoreCollection;

[Collection("EffectChainPresetStore")]
public sealed class EffectChainPresetStoreTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"effect-chain-presets-{Guid.NewGuid():N}.json");

    public EffectChainPresetStoreTests() => EffectChainPresetStore.PathOverride = _tempFile;

    public void Dispose()
    {
        EffectChainPresetStore.PathOverride = null;
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void AddOrReplace_ThenLoad_RoundTripsChainJson()
    {
        var chain = new EffectChain();
        chain.Add(new CompressorEffect { Enabled = false });
        var preset = EffectChainPreset.FromChain(chain, "My Chain");

        EffectChainPresetStore.AddOrReplace(preset);
        var loaded = EffectChainPresetStore.Load();

        var back = Assert.Single(loaded);
        Assert.Equal("My Chain", back.Name);

        var restored = new EffectChain();
        back.ApplyTo(restored);
        Assert.Single(restored.Effects);
        Assert.False(restored.Effects[0].Enabled);
    }

    [Fact]
    public void Delete_RemovesUserPreset()
    {
        EffectChainPresetStore.AddOrReplace(new EffectChainPreset("Temp", "{}"));
        EffectChainPresetStore.Delete("Temp");
        Assert.Empty(EffectChainPresetStore.Load());
    }

    [Fact]
    public void Load_NeverReturnsBuiltInNames()
    {
        EffectChainPresetStore.AddOrReplace(new EffectChainPreset(EffectChainPresets.MakeMusicPeakName, "{}"));
        Assert.DoesNotContain(EffectChainPresetStore.Load(), p => p.Name == EffectChainPresets.MakeMusicPeakName);
    }
}
