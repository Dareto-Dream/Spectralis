using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Spectralis.App.Services;
using Spectralis.Core.Metadata;

namespace Spectralis.App.Views;

/// <summary>Single-file tag editor with MusicBrainz lookup and .bak-backed save.</summary>
public partial class TagEditorWindow : Window
{
    private TagEditorModel? _model;
    private byte[]? _coverArt;
    private List<MusicBrainzRecording> _lookupRecordings = [];

    public bool Saved { get; private set; }

    public TagEditorWindow()
    {
        InitializeComponent();
    }

    /// <summary>Opens the editor for one file; returns true when tags were written.</summary>
    public static async Task<bool> EditAsync(Window owner, string path)
    {
        var window = new TagEditorWindow();
        try
        {
            window._model = TagEditorService.Read(path);
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"Tag read failed for {path}.", ex);
            return false;
        }

        window.PopulateFields();
        await window.ShowDialog(owner);
        return window.Saved;
    }

    private void PopulateFields()
    {
        if (_model is null)
        {
            return;
        }

        FileText.Text = _model.FilePath;
        TitleBox.Text = _model.Title ?? "";
        ArtistBox.Text = _model.Artist ?? "";
        AlbumArtistBox.Text = _model.AlbumArtist ?? "";
        AlbumBox.Text = _model.Album ?? "";
        TrackBox.Value = _model.TrackNumber;
        DiscBox.Value = _model.DiscNumber;
        YearBox.Value = _model.Year;
        BpmBox.Value = _model.Bpm;
        GenreBox.Text = _model.Genre ?? "";
        ComposerBox.Text = _model.Composer ?? "";
        CommentBox.Text = _model.Comment ?? "";
        LyricsBox.Text = _model.Lyrics ?? "";
        SetCoverArt(_model.CoverArt);
    }

    private void SetCoverArt(byte[]? bytes)
    {
        _coverArt = bytes;
        if (bytes is { Length: > 0 })
        {
            try
            {
                using var stream = new MemoryStream(bytes);
                CoverImage.Source = new Bitmap(stream);
                return;
            }
            catch
            {
            }
        }

        CoverImage.Source = null;
    }

    private async void OnChooseCover(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose cover image",
            AllowMultiple = false,
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
        });

        var path = files.FirstOrDefault()?.TryGetLocalPath();
        if (path is not null)
        {
            try
            {
                SetCoverArt(await File.ReadAllBytesAsync(path));
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Could not read image: {ex.Message}";
            }
        }
    }

    private void OnRemoveCover(object? sender, RoutedEventArgs e) => SetCoverArt(null);

    private async void OnLookup(object? sender, RoutedEventArgs e)
    {
        LookupStatus.Text = "Searching MusicBrainz...";
        LookupResults.IsVisible = false;
        try
        {
            _lookupRecordings = await MusicBrainzClient.SearchAsync(
                TitleBox.Text ?? "",
                ArtistBox.Text ?? "");
            if (_lookupRecordings.Count == 0)
            {
                LookupStatus.Text = "No matches found.";
                return;
            }

            LookupResults.ItemsSource = _lookupRecordings.Select(r => r.DisplayText).ToList();
            LookupResults.IsVisible = true;
            LookupStatus.Text = $"{_lookupRecordings.Count} matches - double-click to apply.";
        }
        catch (Exception ex)
        {
            LookupStatus.Text = $"Lookup failed: {ex.Message}";
        }
    }

    private async void OnApplyLookupResult(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        var index = LookupResults.SelectedIndex;
        if (index < 0 || index >= _lookupRecordings.Count)
        {
            return;
        }

        var recording = _lookupRecordings[index];
        TitleBox.Text = recording.Title;
        ArtistBox.Text = recording.Artist;
        if (!string.IsNullOrEmpty(recording.Album))
        {
            AlbumBox.Text = recording.Album;
        }

        if (recording.Year > 0)
        {
            YearBox.Value = recording.Year;
        }

        if (recording.TrackNumber > 0)
        {
            TrackBox.Value = recording.TrackNumber;
        }

        LookupStatus.Text = "Fetching cover art...";
        var art = await MusicBrainzClient.FetchCoverArtAsync(recording.ReleaseId);
        if (art is not null)
        {
            SetCoverArt(art);
            LookupStatus.Text = "Applied metadata + cover art.";
        }
        else
        {
            LookupStatus.Text = "Applied metadata (no cover art found).";
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_model is null)
        {
            Close();
            return;
        }

        _model.Title = NullIfEmpty(TitleBox.Text);
        _model.Artist = NullIfEmpty(ArtistBox.Text);
        _model.AlbumArtist = NullIfEmpty(AlbumArtistBox.Text);
        _model.Album = NullIfEmpty(AlbumBox.Text);
        _model.TrackNumber = (uint)(TrackBox.Value ?? 0);
        _model.DiscNumber = (uint)(DiscBox.Value ?? 0);
        _model.Year = (uint)(YearBox.Value ?? 0);
        _model.Bpm = (uint)(BpmBox.Value ?? 0);
        _model.Genre = NullIfEmpty(GenreBox.Text);
        _model.Composer = NullIfEmpty(ComposerBox.Text);
        _model.Comment = NullIfEmpty(CommentBox.Text);
        _model.Lyrics = NullIfEmpty(LyricsBox.Text);
        _model.CoverArt = _coverArt;

        try
        {
            TagEditorService.Write(_model);
            Saved = true;
            Close();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
