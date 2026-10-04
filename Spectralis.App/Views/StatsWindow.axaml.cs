using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Spectralis.App.Stats;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.Views;

/// <summary>Listening stats over the local scrobble history: totals, streaks, top lists.</summary>
public partial class StatsWindow : Window
{
    private List<ScrobbleRecord> _history = [];

    public StatsWindow()
    {
        InitializeComponent();
        PeriodBox.ItemsSource = new[] { "This Week", "This Month", "All Time" };
        Opened += (_, _) =>
        {
            _history = ScrobbleQueue.LoadHistory();
            PeriodBox.SelectedIndex = 2;
        };
    }

    private void OnPeriodChanged(object? sender, SelectionChangedEventArgs e) => Refresh();

    private ListeningStats _stats = ListeningStats.Compute([], DateTime.MinValue);

    private async void OnShareImage(object? sender, RoutedEventArgs e)
    {
        var period = PeriodBox.SelectedItem as string ?? "All Time";
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save your wrapped card…",
            SuggestedFileName = $"spectralis-wrapped-{period.Replace(' ', '-').ToLowerInvariant()}.png",
            FileTypeChoices = [FilePickerFileTypes.ImagePng],
            DefaultExtension = "png",
        });
        if (file is null)
            return;

        var png = WrappedCardRenderer.RenderPng(WrappedSummary.From(_stats, period));
        await using var stream = await file.OpenWriteAsync();
        await stream.WriteAsync(png);
    }

    private void Refresh()
    {
        var since = PeriodBox.SelectedIndex switch
        {
            0 => DateTime.UtcNow.AddDays(-7),
            1 => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.MinValue,
        };

        var stats = ListeningStats.Compute(_history, since);
        _stats = stats;
        ShareButton.IsEnabled = stats.TotalScrobbles > 0;

        ScrobblesText.Text = stats.TotalScrobbles.ToString("N0");
        HoursText.Text = stats.TotalHours.ToString("0.#");
        StreakText.Text = stats.CurrentStreakDays.ToString();
        StreakLabel.Text = $"day streak (best {stats.LongestStreakDays})";

        ArtistsList.ItemsSource = stats.TopArtists
            .Select((a, i) => $"{i + 1}. {a.Artist}  ·  {a.Plays} plays")
            .ToList();
        TracksList.ItemsSource = stats.TopTracks
            .Select((t, i) => $"{i + 1}. {t.Artist} - {t.Title}  ·  {t.Plays} plays")
            .ToList();
    }
}
