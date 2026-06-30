using System.Text.Json.Serialization;
using ReactiveUI;

namespace Spectralis.App.ViewModels;

/// <summary>One slice of the spin wheel: text plus per-entry color/font/weighting overrides.</summary>
public sealed class WheelEntry : ReactiveObject
{
    private string _text = string.Empty;
    private string? _colorHex;
    private string? _fontFamily;
    private decimal _weight = 1.0m;
    private bool _isSettingsOpen;

    public WheelEntry() { }

    public WheelEntry(string text) => _text = text;

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

    /// <summary>Whether the per-entry settings flyout is currently open (UI state, not persisted).</summary>
    [JsonIgnore]
    public bool IsSettingsOpen
    {
        get => _isSettingsOpen;
        set => this.RaiseAndSetIfChanged(ref _isSettingsOpen, value);
    }

    public WheelEntry Clone() => new(Text) { ColorHex = ColorHex, FontFamily = FontFamily, Weight = Weight };
}

/// <summary>A named set of wheel entries saved for later use.</summary>
public sealed class SavedWheel
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("entries")] public List<WheelEntry> Entries { get; set; } = [];
}
