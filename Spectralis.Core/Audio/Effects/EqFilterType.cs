namespace Spectralis.Core.Audio.Effects;

/// <summary>Filter shape for a single parametric-EQ band.</summary>
public enum EqFilterType
{
    /// <summary>Bell / peaking filter — boost or cut around a centre frequency.</summary>
    Peak,

    /// <summary>Low shelf — boost or cut everything below the corner frequency.</summary>
    LowShelf,

    /// <summary>High shelf — boost or cut everything above the corner frequency.</summary>
    HighShelf,

    /// <summary>Low-pass — attenuate content above the cutoff (gain is ignored).</summary>
    LowPass,

    /// <summary>High-pass — attenuate content below the cutoff (gain is ignored).</summary>
    HighPass,

    /// <summary>Notch — narrow band reject at the centre frequency (gain is ignored).</summary>
    Notch,
}
