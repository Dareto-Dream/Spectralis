using System.Text.Json.Serialization;

namespace Spectralis;

internal sealed class ObsOverlayState
{
    public static readonly ObsOverlayState Empty = new();

    [JsonPropertyName("track")]      public ObsTrackState Track { get; init; } = new();
    [JsonPropertyName("playback")]   public ObsPlaybackState Playback { get; init; } = new();
    [JsonPropertyName("lyrics")]     public ObsLyricsState Lyrics { get; init; } = new();
    [JsonPropertyName("queue")]      public ObsQueueItem[] Queue { get; init; } = [];
    [JsonPropertyName("visualizer")] public ObsVisualizerState Visualizer { get; init; } = new();
    [JsonPropertyName("theme")]      public ObsThemeState Theme { get; init; } = new();
    [JsonPropertyName("songWars")]   public ObsSongWarsState SongWars { get; init; } = new();
    /// <summary>Incremented each time the layout changes; browser uses this to know when to re-fetch /layout.</summary>
    [JsonPropertyName("layoutSeq")]  public int LayoutSeq { get; set; }
}

internal sealed class ObsTrackState
{
    [JsonPropertyName("title")]           public string Title { get; init; } = "";
    [JsonPropertyName("artist")]          public string Artist { get; init; } = "";
    [JsonPropertyName("album")]           public string Album { get; init; } = "";
    [JsonPropertyName("durationSeconds")] public double DurationSeconds { get; init; }
    [JsonPropertyName("artworkVersion")]  public string ArtworkVersion { get; init; } = "";
}

internal sealed class ObsPlaybackState
{
    [JsonPropertyName("isPlaying")]       public bool IsPlaying { get; init; }
    [JsonPropertyName("positionSeconds")] public double PositionSeconds { get; init; }
    [JsonPropertyName("volume")]          public double Volume { get; init; }
}

internal sealed class ObsLyricsState
{
    [JsonPropertyName("current")]  public string Current { get; init; } = "";
    [JsonPropertyName("next")]     public string Next { get; init; } = "";
    [JsonPropertyName("progress")] public double Progress { get; init; }
}

internal sealed class ObsQueueItem
{
    [JsonPropertyName("title")]     public string Title { get; init; } = "";
    [JsonPropertyName("artist")]    public string Artist { get; init; } = "";
    [JsonPropertyName("isCurrent")] public bool IsCurrent { get; init; }
}

internal sealed class ObsVisualizerState
{
    [JsonPropertyName("levels")]  public double[] Levels { get; init; } = [];
    [JsonPropertyName("rms")]     public double Rms { get; init; }
    [JsonPropertyName("peak")]    public double Peak { get; init; }
}

internal sealed class ObsThemeState
{
    [JsonPropertyName("accent")]     public string Accent { get; init; } = "#F59E0B";
    [JsonPropertyName("background")] public string Background { get; init; } = "#1A1A1A";
    [JsonPropertyName("foreground")] public string Foreground { get; init; } = "#F5F5F5";
}

internal sealed class ObsSongWarsState
{
    [JsonPropertyName("isActive")]         public bool IsActive { get; init; }
    [JsonPropertyName("tournamentId")]     public string TournamentId { get; init; } = "";
    [JsonPropertyName("name")]             public string Name { get; init; } = "";
    [JsonPropertyName("currentMatchId")]   public string CurrentMatchId { get; init; } = "";
    [JsonPropertyName("highlightMatchId")] public string HighlightMatchId { get; init; } = "";
    [JsonPropertyName("phase")]            public string Phase { get; init; } = "";
    [JsonPropertyName("roundLabel")]       public string RoundLabel { get; init; } = "";
    [JsonPropertyName("focusSlot")]        public string FocusSlot { get; init; } = "";
    [JsonPropertyName("eliminationsUsed")] public int EliminationsUsed { get; init; }
    [JsonPropertyName("maxEliminations")]  public int MaxEliminations { get; init; }
    [JsonPropertyName("submissions")]      public ObsSongWarsSubmission[] Submissions { get; init; } = [];
    [JsonPropertyName("matches")]          public ObsSongWarsMatch[] Matches { get; init; } = [];
    [JsonPropertyName("nextMatch")]        public ObsSongWarsMatch? NextMatch { get; init; }
    [JsonPropertyName("winner")]           public ObsSongWarsSubmission? Winner { get; init; }
}

internal sealed class ObsSongWarsSubmission
{
    [JsonPropertyName("id")]       public string Id { get; init; } = "";
    [JsonPropertyName("title")]    public string Title { get; init; } = "";
    [JsonPropertyName("artist")]   public string Artist { get; init; } = "";
    [JsonPropertyName("seed")]     public int? Seed { get; init; }
    [JsonPropertyName("losses")]   public int Losses { get; init; }
    [JsonPropertyName("status")]   public string Status { get; init; } = "";
}

internal sealed class ObsSongWarsMatch
{
    [JsonPropertyName("id")]              public string Id { get; init; } = "";
    [JsonPropertyName("bracket")]         public string Bracket { get; init; } = "";
    [JsonPropertyName("roundIndex")]      public int RoundIndex { get; init; }
    [JsonPropertyName("roundId")]         public string RoundId { get; init; } = "";
    [JsonPropertyName("roundLabel")]      public string RoundLabel { get; init; } = "";
    [JsonPropertyName("slotAId")]         public string SlotAId { get; init; } = "";
    [JsonPropertyName("slotBId")]         public string SlotBId { get; init; } = "";
    [JsonPropertyName("slotATitle")]      public string SlotATitle { get; init; } = "";
    [JsonPropertyName("slotBTitle")]      public string SlotBTitle { get; init; } = "";
    [JsonPropertyName("slotAArtist")]     public string SlotAArtist { get; init; } = "";
    [JsonPropertyName("slotBArtist")]     public string SlotBArtist { get; init; } = "";
    [JsonPropertyName("phase")]           public string Phase { get; init; } = "";
    [JsonPropertyName("result")]          public string Result { get; init; } = "";
    [JsonPropertyName("winnerId")]        public string WinnerId { get; init; } = "";
    [JsonPropertyName("winnerTitle")]     public string WinnerTitle { get; init; } = "";
    [JsonPropertyName("eliminatedSlot")]  public string EliminatedSlot { get; init; } = "";
    [JsonPropertyName("isCurrent")]       public bool IsCurrent { get; init; }
    [JsonPropertyName("isHighlighted")]   public bool IsHighlighted { get; init; }
}
