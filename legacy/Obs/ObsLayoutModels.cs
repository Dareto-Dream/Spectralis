using System.Text.Json;
using System.Text.Json.Serialization;

namespace Spectralis;

internal static class ObsWidgetType
{
    public const string NowPlaying  = "nowplaying";
    public const string Lyrics      = "lyrics";
    public const string Queue       = "queue";
    public const string Progress    = "progress";
    public const string Visualizer  = "visualizer";
    public const string SongWarsBracket = "songwars-bracket";
}

internal sealed class ObsLayoutWidget
{
    [JsonPropertyName("type")]         public string Type          { get; set; } = ObsWidgetType.NowPlaying;
    /// <summary>X position as fraction of stream width (0–1)</summary>
    [JsonPropertyName("x")]            public double X             { get; set; }
    /// <summary>Y position as fraction of stream height (0–1)</summary>
    [JsonPropertyName("y")]            public double Y             { get; set; }
    [JsonPropertyName("w")]            public double W             { get; set; } = 0.35;
    [JsonPropertyName("h")]            public double H             { get; set; } = 0.12;
    /// <summary>For type=visualizer: "builtin:MirrorSpectrum" or "installed:{id}"</summary>
    [JsonPropertyName("vizKey")]       public string? VizKey       { get; set; }
    [JsonPropertyName("artShape")]     public string ArtShape      { get; set; } = "rounded";
    [JsonPropertyName("showArt")]      public bool   ShowArt       { get; set; } = true;
    [JsonPropertyName("showArtist")]   public bool   ShowArtist    { get; set; } = true;
    [JsonPropertyName("showProgress")] public bool   ShowProgress  { get; set; } = true;
    [JsonPropertyName("showNext")]     public bool   ShowNext      { get; set; } = true;
    [JsonPropertyName("bgOpacity")]    public int    BgOpacity     { get; set; } = 78;
    [JsonPropertyName("radius")]       public int    Radius        { get; set; } = 10;
    [JsonPropertyName("vizIntensity")] public int    VizIntensity  { get; set; } = 100;
    [JsonPropertyName("maxItems")]     public int    MaxItems      { get; set; } = 7;
    /// <summary>CSS hex color override for visualizer tint (null = use theme accent)</summary>
    [JsonPropertyName("colorHex")]     public string? ColorHex     { get; set; }

    public ObsLayoutWidget Clone() => (ObsLayoutWidget)MemberwiseClone();
}

internal sealed class ObsLayout
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("widgets")]      public List<ObsLayoutWidget> Widgets      { get; set; } = [];
    /// <summary>If true, installed visualizers without obs_banner fall back to generic bars instead of hiding.</summary>
    [JsonPropertyName("allowFallback")] public bool                AllowFallback { get; set; }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ObsLayout? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ObsLayout>(json, JsonOptions); }
        catch { return null; }
    }

    public static ObsLayout CreateDefault() => new()
    {
        Widgets =
        [
            new ObsLayoutWidget
            {
                Type = ObsWidgetType.NowPlaying,
                X = 0.02, Y = 0.78, W = 0.30, H = 0.13,
                ShowArt = true, ShowArtist = true, ShowProgress = true
            },
            new ObsLayoutWidget
            {
                Type = ObsWidgetType.Visualizer,
                VizKey = VisualizerChoice.BuiltIn(VisualizerMode.MirrorSpectrum).Key,
                X = 0.02, Y = 0.68, W = 0.30, H = 0.09,
                BgOpacity = 0
            }
        ]
    };
}

internal sealed class ObsPreset
{
    [JsonPropertyName("name")]       public string Name       { get; set; } = "";
    [JsonPropertyName("layoutJson")] public string LayoutJson { get; set; } = "";
    [JsonIgnore]                     public ObsLayout? Layout  => ObsLayout.FromJson(LayoutJson);
}

internal static class BuiltInObsPresets
{
    private static string VK(VisualizerMode m) => VisualizerChoice.BuiltIn(m).Key;

    public static readonly IReadOnlyList<ObsPreset> All =
    [
        P("Default", [
            new() { Type=ObsWidgetType.Visualizer, VizKey=VK(VisualizerMode.MirrorSpectrum), X=0.02, Y=0.65, W=0.30, H=0.09, BgOpacity=0 },
            new() { Type=ObsWidgetType.NowPlaying,  X=0.02, Y=0.75, W=0.30, H=0.13 },
        ]),
        P("Now Playing", [
            new() { Type=ObsWidgetType.NowPlaying, X=0.32, Y=0.74, W=0.36, H=0.18 },
        ]),
        P("Stream Full", [
            new() { Type=ObsWidgetType.NowPlaying,  X=0.02, Y=0.78, W=0.30, H=0.13 },
            new() { Type=ObsWidgetType.Lyrics,      X=0.25, Y=0.88, W=0.50, H=0.09, BgOpacity=0 },
            new() { Type=ObsWidgetType.Queue,       X=0.72, Y=0.04, W=0.26, H=0.35 },
            new() { Type=ObsWidgetType.Visualizer,  VizKey=VK(VisualizerMode.MirrorSpectrum), X=0.02, Y=0.68, W=0.30, H=0.09, BgOpacity=0 },
            new() { Type=ObsWidgetType.Progress,    X=0.00, Y=0.97, W=1.00, H=0.03, BgOpacity=0 },
        ]),
        P("Song Wars Full", [
            new() { Type=ObsWidgetType.SongWarsBracket, X=0.03, Y=0.05, W=0.94, H=0.72, BgOpacity=82, Radius=8 },
            new() { Type=ObsWidgetType.NowPlaying,      X=0.03, Y=0.80, W=0.30, H=0.13 },
            new() { Type=ObsWidgetType.Visualizer,      VizKey=VK(VisualizerMode.MirrorSpectrum), X=0.35, Y=0.83, W=0.30, H=0.09, BgOpacity=0 },
            new() { Type=ObsWidgetType.Progress,        X=0.00, Y=0.97, W=1.00, H=0.03, BgOpacity=0 },
        ]),
        P("Song Wars Bracket", [
            new() { Type=ObsWidgetType.SongWarsBracket, X=0.02, Y=0.04, W=0.96, H=0.90, BgOpacity=72, Radius=8 },
        ]),
        P("Karaoke", [
            new() { Type=ObsWidgetType.NowPlaying, X=0.02, Y=0.04, W=0.25, H=0.11, ShowProgress=false },
            new() { Type=ObsWidgetType.Lyrics,     X=0.15, Y=0.80, W=0.70, H=0.14, BgOpacity=0 },
            new() { Type=ObsWidgetType.Visualizer, VizKey=VK(VisualizerMode.Waveform), X=0.15, Y=0.94, W=0.70, H=0.05, BgOpacity=0 },
        ]),
        P("Spectrum Bar", [
            new() { Type=ObsWidgetType.NowPlaying,  X=0.02, Y=0.73, W=0.25, H=0.13 },
            new() { Type=ObsWidgetType.Visualizer,  VizKey=VK(VisualizerMode.MirrorSpectrum), X=0.00, Y=0.88, W=1.00, H=0.12, BgOpacity=0 },
        ]),
        P("Compact", [
            new() { Type=ObsWidgetType.NowPlaying, X=0.02, Y=0.82, W=0.22, H=0.12 },
            new() { Type=ObsWidgetType.Progress,   X=0.00, Y=0.97, W=1.00, H=0.03, BgOpacity=0 },
        ]),
        P("Queue Focus", [
            new() { Type=ObsWidgetType.NowPlaying, X=0.02, Y=0.04, W=0.28, H=0.14 },
            new() { Type=ObsWidgetType.Queue,      X=0.72, Y=0.04, W=0.26, H=0.55 },
            new() { Type=ObsWidgetType.Progress,   X=0.00, Y=0.97, W=1.00, H=0.03, BgOpacity=0 },
        ]),
        P("Stage", [
            new() { Type=ObsWidgetType.Visualizer, VizKey=VK(VisualizerMode.RadialSpectrum), X=0.25, Y=0.22, W=0.50, H=0.58, BgOpacity=0 },
            new() { Type=ObsWidgetType.NowPlaying, X=0.02, Y=0.82, W=0.25, H=0.12 },
            new() { Type=ObsWidgetType.Progress,   X=0.00, Y=0.97, W=1.00, H=0.03, BgOpacity=0 },
        ]),
    ];

    private static ObsPreset P(string name, List<ObsLayoutWidget> widgets) => new()
    {
        Name = name,
        LayoutJson = new ObsLayout { Widgets = widgets }.ToJson()
    };
}
