using Avalonia.Controls;
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

    private void Refresh()
    {
        var since = PeriodBox.SelectedIndex switch
        {
            0 => DateTime.UtcNow.AddDays(-7),
            1 => DateTime.UtcNow.AddMonths(-1),
            _ => DateTime.MinValue,
        };

        var stats = ListeningStats.Compute(_history, since);

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
