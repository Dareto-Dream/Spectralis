using Spectralis.Core.Platform;

namespace Spectralis.App.Services;

/// <summary>
/// Opt-in previews. A beta feature is offered to people on the Beta update channel and in beta
/// builds themselves, is off until its own switch is turned on, and reads as off everywhere else,
/// so leaving the beta channel also leaves its features without having to untick anything.
///
/// To add one: a bool on <see cref="AppSettings"/>, a property here that ANDs it with
/// <see cref="Available"/>, and a checkbox in the Beta features block of the Settings view.
/// </summary>
public static class BetaFeatures
{
    private static volatile bool _spotifyMatchup;

    /// <summary>Whether beta features are offered at all for these settings and this build.</summary>
    public static bool Available(AppSettings settings) =>
        VelopackUpdateService.IsBetaChannel(settings.UpdateChannel) ||
        ReleaseVersion.IsPrerelease(DiagnosticsSnapshot.CurrentVersion);

    /// <summary>
    /// Spotify matchup: play Spotify tracks from a matched YouTube upload instead of the Spotify
    /// player. Read on the playback path, so it is a cached flag rather than a settings load.
    /// </summary>
    public static bool SpotifyMatchup => _spotifyMatchup;

    /// <summary>Call at startup and whenever the channel or a feature switch changes.</summary>
    public static void Apply(AppSettings settings)
    {
        _spotifyMatchup = Available(settings) && settings.SpotifyMatchupEnabled;
    }
}
