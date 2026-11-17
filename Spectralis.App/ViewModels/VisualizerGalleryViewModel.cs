using ReactiveUI;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.ViewModels;

public sealed record VisualizerFamily(string Name, string Category, string Description, IReadOnlyList<VisualizerOption> Variants, byte[]? ArtworkBytes = null)
{
    public VisualizerMode PreviewMode => Variants[0].Mode;
    public string VariantSummary => Variants.Count == 1 ? Category : $"{Category} · {Variants.Count} variations";
}

public sealed class VisualizerGalleryViewModel : ViewModelBase
{
    private readonly IReadOnlyList<VisualizerFamily> _families;
    private string _search = "";
    private string _category = "All";
    private VisualizerFamily? _selectedFamily;
    private VisualizerOption? _selectedVariant;

    public VisualizerGalleryViewModel(IEnumerable<VisualizerOption> options, VisualizerOption? selected = null, byte[]? artwork = null)
    {
        _families = BuildFamilies(options.ToArray(), artwork);
        HasArtwork = artwork is { Length: > 0 };
        SelectedFamily = _families.FirstOrDefault(f => selected is not null && f.Variants.Contains(selected)) ?? _families.FirstOrDefault();
        if (selected is not null && SelectedFamily?.Variants.Contains(selected) == true) SelectedVariant = selected;
    }

    public IReadOnlyList<string> Categories { get; } = ["All", "Immersive", "Spectrum", "Artwork", "Meters", "Custom"];
    public IReadOnlyList<VisualizerFamily> Families => _families.Where(f =>
        (Category == "All" || f.Category == Category) &&
        (string.IsNullOrWhiteSpace(Search) || $"{f.Name} {f.Description} {string.Join(' ', f.Variants.Select(v => v.Label))}".Contains(Search.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
    public bool IsEmpty => Families.Count == 0;
    public string Search { get => _search; set { this.RaiseAndSetIfChanged(ref _search, value); Refresh(); } }
    public string Category { get => _category; set { this.RaiseAndSetIfChanged(ref _category, value); Refresh(); } }
    public VisualizerFamily? SelectedFamily
    {
        get => _selectedFamily;
        set
        {
            if (value is null || value == _selectedFamily) return;
            this.RaiseAndSetIfChanged(ref _selectedFamily, value);
            SelectedVariant = value.Variants[0];
            this.RaisePropertyChanged(nameof(HasVariations));
        }
    }
    public VisualizerOption? SelectedVariant
    {
        get => _selectedVariant;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedVariant, value);
            this.RaisePropertyChanged(nameof(PreviewMode));
            this.RaisePropertyChanged(nameof(CanPreview));
            this.RaisePropertyChanged(nameof(PreviewNote));
            this.RaisePropertyChanged(nameof(PreviewUnavailableText));
        }
    }
    public bool HasVariations => SelectedFamily?.Variants.Count > 1;
    public bool HasArtwork { get; init; }
    public bool CanPreview => SelectedVariant is { Script: null, Installed: null }
        && (!VisualizerCatalog.GetDefinition(PreviewMode).RequiresAlbumArt || HasArtwork);
    public string PreviewUnavailableText => SelectedVariant is { Script: null, Installed: null }
        ? "Play a track with artwork to preview." : "Custom visualizer";
    public string PreviewNote => SelectedVariant is { Script: not null } or { Installed: not null } ? "Custom content opens when selected."
        : VisualizerCatalog.GetDefinition(PreviewMode).RequiresAlbumArt ? "Uses the current track’s artwork."
        : VisualizerCatalog.GetDefinition(PreviewMode).RequiresMidi ? "Reacts to MIDI notes."
        : "";
    public VisualizerMode PreviewMode => SelectedVariant?.Mode ?? VisualizerMode.Wormhole;

    private void Refresh()
    {
        this.RaisePropertyChanged(nameof(Families));
        this.RaisePropertyChanged(nameof(IsEmpty));
        if (Families.Count > 0 && !Families.Contains(SelectedFamily)) SelectedFamily = Families[0];
    }

    private static IReadOnlyList<VisualizerFamily> BuildFamilies(IReadOnlyList<VisualizerOption> options, byte[]? artwork)
    {
        var result = new List<VisualizerFamily>();
        void Add(string name, string category, string description, params VisualizerMode[] modes)
        {
            var variants = modes.SelectMany(m => options.Where(o => o.Mode == m && o.Script is null && o.Installed is null)).ToArray();
            if (variants.Length > 0) result.Add(new(name, category, description, variants, artwork));
        }
        Add("Wormhole", "Immersive", "A twisting tunnel pulled forward by the music.", VisualizerMode.Wormhole);
        Add("Silk", "Immersive", "Ribbons fold through space with every phrase.", VisualizerMode.Silk);
        Add("Starfield", "Immersive", "A drifting field of stars with bass-driven trails.", VisualizerMode.Starfield);
        Add("Orbital", "Immersive", "A rotating particle sculpture shaped by the spectrum.", VisualizerMode.Orbital);
        Add("Spectrum", "Spectrum", "Frequency bars, radial rings, or a flowing spectrum.", VisualizerMode.MirrorSpectrum, VisualizerMode.Spectrum, VisualizerMode.RadialSpectrum, VisualizerMode.SpectrumWave);
        Add("Waveforms", "Spectrum", "Follow the signal as a flowing wave or an oscilloscope trace.", VisualizerMode.Oscilloscope, VisualizerMode.Waveform);
        Add("3D spectrum", "Immersive", "Turn frequency into a landscape or a rotating sphere.", VisualizerMode.Graph3D, VisualizerMode.Sphere3D);
        Add("Dancing Colors", "Immersive", "Soft fields of color that respond to the music.", VisualizerMode.DancingColors);
        Add("Artwork", "Artwork", "Keep the cover in focus, still or spinning.", VisualizerMode.AlbumCover, VisualizerMode.SpinningDisk);
        Add("Meters", "Meters", "Watch level, loudness, and stereo balance.", VisualizerMode.VUMeter, VisualizerMode.LoudnessMeter, VisualizerMode.Stereometer);
        Add("Spectrogram", "Spectrum", "See the frequency history unfold.", VisualizerMode.Spectrogram);
        Add("Piano Roll", "Meters", "Follow the notes in a MIDI performance.", VisualizerMode.PianoRoll);
        foreach (var custom in options.Where(o => o.Script is not null || o.Installed is not null))
            result.Add(new(custom.Label, "Custom", "An installed visualizer from your collection.", [custom]));
        return result;
    }
}
