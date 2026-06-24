namespace Spectralis;

public partial class Form1
{
    private void SyncNativeExternalPlaybackState()
    {
        if (soundCloudUsesNativePlayback && soundCloudCurrentTrack is not null)
        {
            soundCloudPositionSeconds = engine.GetPosition();
            if (engine.GetLength() > 0)
                soundCloudDurationSeconds = engine.GetLength();
            var wasPlayingSC = soundCloudIsPlaying;
            soundCloudIsPlaying = engine.IsPlaying;
            if (wasPlayingSC && !soundCloudIsPlaying &&
                HasNativeExternalTrackEnded(soundCloudPositionSeconds, soundCloudDurationSeconds))
            {
                OnSoundCloudNaturalEnd();
            }
        }

        if (sunoUsesNativePlayback && sunoCurrentTrack is not null)
        {
            sunoPositionSeconds = engine.GetPosition();
            if (engine.GetLength() > 0)
                UpdateSunoDuration(engine.GetLength());
            var wasPlayingSuno = sunoIsPlaying;
            sunoIsPlaying = engine.IsPlaying;
            if (wasPlayingSuno && !sunoIsPlaying &&
                HasNativeExternalTrackEnded(sunoPositionSeconds, sunoDurationSeconds))
            {
                OnSunoNaturalEnd();
            }
        }

        if (youTubeCurrentTrack is not null)
        {
            youTubePositionSeconds = engine.GetPosition();
            if (engine.GetLength() > 0)
                youTubeDurationSeconds = engine.GetLength();
            var wasPlaying = youTubeIsPlaying;
            youTubeIsPlaying = engine.IsPlaying;
            if (wasPlaying && !youTubeIsPlaying &&
                HasNativeExternalTrackEnded(youTubePositionSeconds, youTubeDurationSeconds))
            {
                OnYouTubeNaturalEnd();
            }
        }
    }

    private static bool HasNativeExternalTrackEnded(float positionSeconds, float durationSeconds) =>
        durationSeconds > 0 && positionSeconds >= Math.Max(0, durationSeconds - 0.25f);
}
