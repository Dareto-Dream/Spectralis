using Spectralis.App.Services;
using Spectralis.Core.Audio.Effects;
using Xunit;

namespace Spectralis.Tests.App;

/// <summary>
/// <see cref="EqPresetStore.PathOverride"/> is process-global, so the tests that
/// set it must not run in parallel with each other.
/// </summary>
[CollectionDefinition("EqPresetStore", DisableParallelization = true)]
public sealed class EqPresetStoreCollection;

[Collection("EqPresetStore")]
public sealed class EqPresetStoreTests : IDisposable
{
    private readonly string _tempFile = Path.Combine(Path.GetTempPath(), $"eq-presets-{Guid.NewGuid():N}.json");

    public EqPresetStoreTests() => EqPresetStore.PathOverride = _tempFile;

    public void Dispose()
    {
        EqPresetStore.PathOverride = null;
        if (File.Exists(_tempFile))
        {
            File.Delete(_tempFile);
        }
    }

    [Fact]
    public void AddOrReplace_ThenLoad_RoundTripsBands()
    {
        var preset = new EqPreset("My Curve", -2f, 1.5f,
        [
            new EqBand(80f, 4.5f, 0.7f, EqFilterType.LowShelf),
            new EqBand(2500f, -3f, 2.2f, EqFilterType.Peak),
            new EqBand(12000f, 6f, 0.9f, EqFilterType.HighShelf, Enabled: false),
        ]);

        EqPresetStore.AddOrReplace(preset);
        var loaded = EqPresetStore.Load();

        var back = Assert.Single(loaded);
        Assert.Equal("My Curve", back.Name);
        Assert.Equal(-2f, back.PreampDb);
        Assert.Equal(1.5f, back.OutputGainDb);
        Assert.Equal(3, back.Bands.Count);
        Assert.Equal(EqFilterType.LowShelf, back.Bands[0].Type);
        Assert.False(back.Bands[2].Enabled);
    }

    [Fact]
    public void Delete_RemovesUserPreset()
    {
        EqPresetStore.AddOrReplace(new EqPreset("Temp", 0, 0, [new EqBand(1000f, 1f, 1f, EqFilterType.Peak)]));
        EqPresetStore.Delete("Temp");
        Assert.Empty(EqPresetStore.Load());
    }

    [Fact]
    public void Load_NeverReturnsBuiltInNames()
    {
        EqPresetStore.AddOrReplace(new EqPreset("Rock", 0, 0, [new EqBand(1000f, 1f, 1f, EqFilterType.Peak)]));
        Assert.DoesNotContain(EqPresetStore.Load(), p => p.Name == "Rock");
    }
}
