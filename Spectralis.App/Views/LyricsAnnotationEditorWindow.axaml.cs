using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.Views;

public partial class LyricsAnnotationEditorWindow : Window
{
    private readonly LyricsDocument _lyrics;
    private readonly string _annotationPath;
    private readonly Dictionary<string, string> _annotations;
    private readonly List<Border> _rowBorders = [];
    private int _selectedIndex = -1;
    private bool _suppressTextChange;

    public LyricsAnnotationEditorWindow(
        string trackTitle,
        LyricsDocument lyrics,
        string audioPath)
    {
        _lyrics = lyrics;
        _annotationPath = Path.ChangeExtension(audioPath, ".lrc.json");
        _annotations = LyricsLoader.LoadAnnotations(audioPath);
        InitializeComponent();

        TrackTitleLabel.Text = trackTitle;
        SubtitleLabel.Text = $"{_annotationPath}";

        BuildRows();
    }

    private void BuildRows()
    {
        var panel = new StackPanel { Spacing = 0 };

        for (var i = 0; i < _lyrics.Lines.Count; i++)
        {
            var line = _lyrics.Lines[i];
            var key = LyricsExplanationParser.TimestampKey(line.StartTime);
            var hasNote = _annotations.ContainsKey(key) && !string.IsNullOrWhiteSpace(_annotations[key]);

            var timestamp = new TextBlock
            {
                Text = FormatTimestamp(line.StartTime),
                Width = 70,
                VerticalAlignment = VerticalAlignment.Top,
            };
            timestamp.Classes.Add("muted");

            var lineText = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Top,
            };

            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*") };
            row.Children.Add(timestamp);
            Grid.SetColumn(lineText, 1);
            row.Children.Add(lineText);

            var border = new Border { Child = row };
            border.Classes.Add("annot-row");
            if (hasNote) border.Classes.Add("has-note");

            var capturedIndex = i;
            border.PointerPressed += (_, _) => SelectRow(capturedIndex);

            _rowBorders.Add(border);
            panel.Children.Add(border);
        }

        LyricsScroll.Content = panel;
    }

    private void SelectRow(int index)
    {
        if (_selectedIndex >= 0 && _selectedIndex < _rowBorders.Count)
        {
            _rowBorders[_selectedIndex].Classes.Remove("selected");
        }

        _selectedIndex = index;
        _rowBorders[index].Classes.Add("selected");

        var line = _lyrics.Lines[index];
        SelectedLineLabel.Text = $"{FormatTimestamp(line.StartTime)}  {line.Text}";
        SelectedLineLabel.Classes.Remove("secondary");

        var key = LyricsExplanationParser.TimestampKey(line.StartTime);
        _suppressTextChange = true;
        AnnotationTextBox.Text = _annotations.TryGetValue(key, out var existing) ? existing : string.Empty;
        _suppressTextChange = false;
        AnnotationTextBox.IsEnabled = true;
    }

    private void OnAnnotationTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressTextChange || _selectedIndex < 0) return;

        var line = _lyrics.Lines[_selectedIndex];
        var key = LyricsExplanationParser.TimestampKey(line.StartTime);
        var text = AnnotationTextBox.Text ?? string.Empty;

        if (string.IsNullOrWhiteSpace(text))
        {
            _annotations.Remove(key);
            _rowBorders[_selectedIndex].Classes.Remove("has-note");
        }
        else
        {
            _annotations[key] = text;
            if (!_rowBorders[_selectedIndex].Classes.Contains("has-note"))
                _rowBorders[_selectedIndex].Classes.Add("has-note");
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            LyricsExplanationParser.Save(_annotationPath, _annotations);
            var count = _annotations.Count(static kvp => !string.IsNullOrWhiteSpace(kvp.Value));
            StatusLabel.Text = $"Saved {count} annotation{(count == 1 ? "" : "s")}.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Save failed: {ex.Message}";
        }
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    private static string FormatTimestamp(double seconds)
    {
        var t = TimeSpan.FromSeconds(seconds);
        return $"{(int)t.TotalMinutes:D2}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}";
    }
}
