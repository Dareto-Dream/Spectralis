using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;
using Spectralis.Core.Metadata;

namespace Spectralis.App.Views;

/// <summary>Batch tag editor: checked fields apply to every selected file (.bak-backed).</summary>
public partial class BatchTagEditorWindow : Window
{
    private List<TagEditorModel> _models = [];

    public bool Saved { get; private set; }

    public BatchTagEditorWindow()
    {
        InitializeComponent();
    }

    public static async Task<bool> EditAsync(Window owner, IReadOnlyList<string> paths)
    {
        var window = new BatchTagEditorWindow();
        foreach (var path in paths)
        {
            try
            {
                window._models.Add(TagEditorService.Read(path));
            }
            catch (Exception ex)
            {
                SpectralisLog.Error($"Tag read failed for {path}.", ex);
            }
        }

        if (window._models.Count == 0)
        {
            return false;
        }

        window.PopulateFields();
        await window.ShowDialog(owner);
        return window.Saved;
    }

    private void PopulateFields()
    {
        HeaderText.Text = _models.Count == 1
            ? "Editing 1 file"
            : $"Editing {_models.Count} files";

        // Prefill with the shared value when every file agrees; mixed values stay blank.
        ArtistBox.Text = SharedText(m => m.Artist);
        AlbumArtistBox.Text = SharedText(m => m.AlbumArtist);
        AlbumBox.Text = SharedText(m => m.Album);
        GenreBox.Text = SharedText(m => m.Genre);
        ComposerBox.Text = SharedText(m => m.Composer);
        CommentBox.Text = SharedText(m => m.Comment);
        YearBox.Value = SharedNumber(m => m.Year);
        BpmBox.Value = SharedNumber(m => m.Bpm);
    }

    private string SharedText(Func<TagEditorModel, string?> get)
    {
        var distinct = _models.Select(get).Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] ?? "" : "";
    }

    private uint SharedNumber(Func<TagEditorModel, uint> get)
    {
        var distinct = _models.Select(get).Distinct().ToList();
        return distinct.Count == 1 ? distinct[0] : 0;
    }

    private void OnSaveAll(object? sender, RoutedEventArgs e)
    {
        var failures = 0;
        foreach (var model in _models)
        {
            if (ArtistCheck.IsChecked == true)
            {
                model.Artist = NullIfEmpty(ArtistBox.Text);
            }

            if (AlbumArtistCheck.IsChecked == true)
            {
                model.AlbumArtist = NullIfEmpty(AlbumArtistBox.Text);
            }

            if (AlbumCheck.IsChecked == true)
            {
                model.Album = NullIfEmpty(AlbumBox.Text);
            }

            if (GenreCheck.IsChecked == true)
            {
                model.Genre = NullIfEmpty(GenreBox.Text);
            }

            if (ComposerCheck.IsChecked == true)
            {
                model.Composer = NullIfEmpty(ComposerBox.Text);
            }

            if (CommentCheck.IsChecked == true)
            {
                model.Comment = NullIfEmpty(CommentBox.Text);
            }

            if (YearCheck.IsChecked == true)
            {
                model.Year = (uint)(YearBox.Value ?? 0);
            }

            if (BpmCheck.IsChecked == true)
            {
                model.Bpm = (uint)(BpmBox.Value ?? 0);
            }

            try
            {
                TagEditorService.Write(model);
            }
            catch (Exception ex)
            {
                failures++;
                SpectralisLog.Error($"Batch tag write failed for {model.FilePath}.", ex);
            }
        }

        if (failures > 0)
        {
            StatusText.Text = $"{failures} of {_models.Count} files failed to save.";
            Saved = failures < _models.Count;
            return;
        }

        Saved = true;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private static string? NullIfEmpty(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
