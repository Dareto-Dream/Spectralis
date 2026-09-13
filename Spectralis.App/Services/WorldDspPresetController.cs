using Spectralis.Core.Audio.Effects;

namespace Spectralis.App.Services;

/// <summary>
/// Applies and reverts a capsule/world-registered DSP preset on top of the user's own
/// effects rack. The preset is materialized through the same <see cref="EffectChainState"/>
/// serialization the user-facing chain presets already use, so a world can only ever
/// build effects <see cref="EffectChain.CreateEffect"/> already knows about — no arbitrary
/// DSP injection. Non-destructive: the user's rack is snapshotted before the first apply
/// and restored verbatim on <see cref="RevertToUser"/>.
/// </summary>
public sealed class WorldDspPresetController
{
    private readonly EffectChain _chain;
    private string? _userSnapshotJson;

    public WorldDspPresetController(EffectChain chain) => _chain = chain;

    /// <summary>True while a world preset is applied (i.e. a revert is pending).</summary>
    public bool IsActive => _userSnapshotJson is not null;

    /// <summary>
    /// Applies <paramref name="presetChainJson"/> (the same shape <see cref="EffectChainState.Serialize"/>
    /// produces) as the active rack. Snapshots the user's current rack first, but only on the
    /// first call while active — a second registration while one is already active replaces the
    /// preset without re-snapshotting (which would otherwise capture the previous world preset
    /// instead of the user's own chain).
    /// </summary>
    public void ApplyWorldPreset(string presetChainJson)
    {
        if (string.IsNullOrWhiteSpace(presetChainJson))
        {
            return;
        }

        _userSnapshotJson ??= EffectChainState.Serialize(_chain);

        _chain.IsWorldManaged = true;
        EffectChainState.Restore(_chain, presetChainJson);
    }

    /// <summary>Restores the user's rack from the snapshot taken by the first <see cref="ApplyWorldPreset"/> call.</summary>
    public void RevertToUser()
    {
        if (_userSnapshotJson is null)
        {
            return;
        }

        var snapshot = _userSnapshotJson;
        _userSnapshotJson = null;
        EffectChainState.Restore(_chain, snapshot);
        _chain.IsWorldManaged = false;
    }
}
