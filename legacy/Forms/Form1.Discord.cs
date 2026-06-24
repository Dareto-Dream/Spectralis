namespace Spectralis;

public partial class Form1
{
    private void SyncDiscordRichPresenceState()
    {
        var track = IsSpotifyActive ? spotifyCurrentTrack : IsSunoActive ? sunoCurrentTrack : engine.CurrentTrack;
        var isPlaying = IsSpotifyActive ? spotifyIsPlaying : IsSunoActive ? sunoIsPlaying : engine.IsPlaying;
        var position = IsSpotifyActive ? spotifyPositionSeconds : IsSunoActive ? sunoPositionSeconds : engine.GetPosition();
        var duration = IsSpotifyActive ? spotifyDurationSeconds : IsSunoActive ? sunoDurationSeconds : engine.GetLength();

        discordRichPresence.SetEnabled(appSettings.EnableDiscordRichPresence);
        discordRichPresence.Update(
            track,
            isPlaying,
            TimeSpan.FromSeconds(position),
            TimeSpan.FromSeconds(duration),
            HasJoinedSharedPlayActivity ? null : GetDiscordListenTogetherUrl(),
            IsSpotifyActive || IsSunoActive ? 0 : queue.CurrentIndex + 1,
            IsSpotifyActive || IsSunoActive ? 0 : queue.Count);
    }

    private string? GetDiscordListenTogetherUrl()
    {
        var joinUrl = sharedPlay.GetJoinUrl();
        return string.IsNullOrWhiteSpace(joinUrl)
            ? null
            : SharedPlayDefaults.ConvertToDiscordActivityJoinUrl(joinUrl);
    }
}
