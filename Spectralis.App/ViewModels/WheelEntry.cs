using System.Text.Json.Serialization;
using ReactiveUI;

namespace Spectralis.App.ViewModels;

/// <summary>What a wheel slice resolves to when it wins the spin.</summary>
public enum WheelEntryKind
{
    /// <summary>Plain label — the spin just announces the text (original behavior).</summary>
    Text,

    /// <summary>A single track. <see cref="WheelEntry.Ref"/> is a local file path or a
    /// "spotify:track:..." uri; winning the spin offers to play it.</summary>
    Song,

    /// <summary>A saved playlist. <see cref="WheelEntry.Ref"/> is the playlist's Guid;
    /// winning the spin offers to play the whole playlist.</summary>
    Playlist,
}

/// <summary>One slice of the spin wheel: a label plus per-entry color/font/weighting
/// overrides, and — for a music wheel — the song or playlist it stands for.</summary>
public sealed class WheelEntry : ReactiveObject
{
    private string _text = string.Empty;
    private string? _colorHex;
    private string? _fontFamily;
    private decimal _weight = 1.0m;
    private bool _isSettingsOpen;
    private WheelEntryKind _kind = WheelEntryKind.Text;
    private string? _reference;
    private string? _subtitle;

    public WheelEntry() { }

    public WheelEntry(string text) => _text = text;

    public WheelEntry(string text, WheelEntryKind kind, string? reference, string? subtitle = null)
    {
        _text = text;
        _kind = kind;
        _reference = reference;
        _subtitle = subtitle;
    }

    [JsonPropertyName("text")]
    public string Text
    {
        get => _text;
        set => this.RaiseAndSetIfChanged(ref _text, value);
    }

    /// <summary>CSS hex color override for this slice (null = use the default palette color).</summary>
    [JsonPropertyName("colorHex")]
    public string? ColorHex
    {
        get => _colorHex;
        set => this.RaiseAndSetIfChanged(ref _colorHex, value);
    }

    /// <summary>Font family override for this slice's label (null = use the wheel default font).</summary>
    [JsonPropertyName("fontFamily")]
    public string? FontFamily
    {
        get => _fontFamily;
        set => this.RaiseAndSetIfChanged(ref _fontFamily, value);
    }

    /// <summary>Relative spin weighting. Larger values are more likely to be picked and get a bigger slice.</summary>
    [JsonPropertyName("weight")]
    public decimal Weight
    {
        get => _weight;
        set => this.RaiseAndSetIfChanged(ref _weight, value <= 0 ? 1.0m : value);
    }

    /// <summary>Whether this slice is a plain label, a song, or a playlist.</summary>
    [JsonPropertyName("kind")]
    public WheelEntryKind Kind
    {
        get => _kind;
        set
        {
            this.RaiseAndSetIfChanged(ref _kind, value);
            this.RaisePropertyChanged(nameof(IsMusic));
        }
    }

    /// <summary>For <see cref="WheelEntryKind.Song"/>: a local path or "spotify:track:..." uri.
    /// For <see cref="WheelEntryKind.Playlist"/>: the playlist Guid as a string. Null for text.</summary>
    [JsonPropertyName("ref")]
    public string? Ref
    {
        get => _reference;
        set => this.RaiseAndSetIfChanged(ref _reference, value);
    }

    /// <summary>Secondary line shown next to the entry (artist for a song, track count for a
    /// playlist). Display only — never used for playback.</summary>
    [JsonPropertyName("subtitle")]
    public string? Subtitle
    {
        get => _subtitle;
        set
        {
            this.RaiseAndSetIfChanged(ref _subtitle, value);
            this.RaisePropertyChanged(nameof(HasSubtitle));
        }
    }

    [JsonIgnore]
    public bool IsMusic => _kind is WheelEntryKind.Song or WheelEntryKind.Playlist;

    [JsonIgnore]
    public bool HasSubtitle => !string.IsNullOrWhiteSpace(_subtitle);

    /// <summary>Whether the per-entry settings flyout is currently open (UI state, not persisted).</summary>
    [JsonIgnore]
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => this.RaiseAndSetIfChanged(ref _isSettingsOpen, value);
    }

    public WheelEntry Clone() => new(Text, Kind, Ref, Subtitle)
    {
        ColorHex = ColorHex,
        FontFamily = FontFamily,
        Weight = Weight,
    };
}

/// <summary>A named set of wheel entries saved for later use.</summary>
public sealed class SavedWheel
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;

    /// <summary>Which randomizer tab the wheel belongs to: "text", "songs", or "playlists".
    /// Older saves without this field load as a text wheel.</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = RandomizerMode.Text;

    [JsonPropertyName("entries")] public List<WheelEntry> Entries { get; set; } = [];
}
