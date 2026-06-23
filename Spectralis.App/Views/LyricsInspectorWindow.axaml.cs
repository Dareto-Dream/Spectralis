using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.Views;

public partial class LyricsInspectorWindow : Window
{
    private readonly LyricsDocument _lyrics;
    private readonly string? _trackPath;
    private LyricsAnnotationEditorWindow? _annotationEditor;
    private Border? _selectedRow;

    public LyricsInspectorWindow(
        string trackTitle,
        string? artist,
        string? album,
        LyricsDocument lyrics,
        double positionSeconds,
        string? trackPath = null)
    {
        _lyrics = lyrics;
        _trackPath = trackPath;
        InitializeComponent();

        TrackTitleLabel.Text = trackTitle;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(artist)) parts.Add(artist);
        if (!string.IsNullOrWhiteSpace(album)) parts.Add(album);
        var annotatedCount = lyrics.Lines.Count(static l => !string.IsNullOrWhiteSpace(l.Explanation));
        parts.Add($"{annotatedCount} annotation{(annotatedCount == 1 ? "" : "s")}");
        parts.Add($"{lyrics.Lines.Count} line{(lyrics.Lines.Count == 1 ? "" : "s")}");
        if (!string.IsNullOrWhiteSpace(lyrics.SourceLabel)) parts.Add(lyrics.SourceLabel);
        SubtitleLabel.Text = string.Join("  /  ", parts);

        BuildRows(positionSeconds);
        Opened += (_, _) => ScrollToCurrentLine(positionSeconds);

        if (trackPath is not null && lyrics.HasLines)
            EditAnnotationsButton.IsVisible = true;
    }

    private void BuildRows(double positionSeconds)
    {
        var currentIndex = _lyrics.FindLineIndex(positionSeconds);
        var rows = new List<Border>();

        for (var i = 0; i < _lyrics.Lines.Count; i++)
        {
            var line = _lyrics.Lines[i];
            var isCurrent = i == currentIndex;
            var hasAnnotation = !string.IsNullOrWhiteSpace(line.Explanation);

            var timestamp = new TextBlock
            {
                Text = FormatTimestamp(line.StartTime),
                Width = 72,
                VerticalAlignment = VerticalAlignment.Top,
            };
            timestamp.Classes.Add("muted");

            var lineText = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            };
            if (isCurrent)
            {
                lineText.Classes.Add("active");
                lineText.FontWeight = FontWeight.SemiBold;
            }

            var dotText = new TextBlock
            {
                Text = "●",
                Width = 16,
                VerticalAlignment = VerticalAlignment.Top,
                IsVisible = hasAnnotation,
            };
            dotText.Classes.Add("signal");

            var rowContent = new Grid { ColumnDefinitions = new ColumnDefinitions("72,*,16") };
            rowContent.Children.Add(timestamp);
            Grid.SetColumn(lineText, 1);
            rowContent.Children.Add(lineText);
            Grid.SetColumn(dotText, 2);
            rowContent.Children.Add(dotText);

            var row = new Border { Tag = line };
            row.Classes.Add("inspector-row");
            if (isCurrent) row.Classes.Add("current");
            row.Child = rowContent;

            if (hasAnnotation)
            {
                row.PointerPressed += (_, _) => SelectRow(row);
            }

            rows.Add(row);
        }

        var panel = new StackPanel { Spacing = 2 };
        foreach (var row in rows) panel.Children.Add(row);
        LyricsScroll.Content = panel;

        // Select the first annotated row near the current position
        var initialRow = currentIndex >= 0 && currentIndex < rows.Count && rows[currentIndex].Tag is LyricsLine cl && !string.IsNullOrWhiteSpace(cl.Explanation)
            ? rows[currentIndex]
            : rows.FirstOrDefault(r => r.Tag is LyricsLine l && !string.IsNullOrWhiteSpace(l.Explanation));
        SelectRow(initialRow);
    }

    private void SelectRow(Border? row)
    {
        if (_selectedRow is not null)
            _selectedRow.Classes.Remove("selected");

        _selectedRow = row;
        if (_selectedRow is not null)
            _selectedRow.Classes.Add("selected");

        if (row?.Tag is not LyricsLine line || string.IsNullOrWhiteSpace(line.Explanation))
        {
            AnnotationTitle.Text = string.Empty;
            AnnotationTime.Text = string.Empty;
            AnnotationBody.Text = string.Empty;
            return;
        }

        AnnotationTitle.Text = line.Text;
        AnnotationTime.Text = FormatTimestamp(line.StartTime);
        AnnotationBody.Text = line.Explanation;
    }

    private void ScrollToCurrentLine(double positionSeconds)
    {
        var idx = _lyrics.FindLineIndex(positionSeconds);
        if (LyricsScroll.Content is not StackPanel panel || idx < 0 || idx >= panel.Children.Count)
            return;
        if (panel.Children[idx] is Control target)
            target.BringIntoView();
    }

    private void OnEditAnnotations(object? sender, RoutedEventArgs e)
    {
        if (_trackPath is null) return;
        if (_annotationEditor is { IsVisible: true }) { _annotationEditor.Activate(); return; }
        _annotationEditor = new LyricsAnnotationEditorWindow(TrackTitleLabel.Text ?? string.Empty, _lyrics, _trackPath);
        _annotationEditor.Closed += (_, _) => _annotationEditor = null;
        _annotationEditor.Show(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    private static string FormatTimestamp(double seconds)
    {
        var cs = (long)Math.Round(Math.Max(0, seconds) * 100d, MidpointRounding.AwayFromZero);
        var m = cs / 6000;
        var s = (cs / 100) % 60;
        var c = cs % 100;
        return $"{m:D2}:{s:D2}.{c:D2}";
    }
}
