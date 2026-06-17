using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Scrobbling;

namespace Spectralis.App.ViewModels
{
    public class ScrobbleEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public DateTime PlayedAt { get; set; }
        public bool WasSubmitted { get; set; }

        public string PlayedAtDisplay => PlayedAt.ToLocalTime().ToString("MMM d, HH:mm");
    }

    public partial class ListeningHistoryViewModel : ObservableObject
    {
        private readonly string _historyPath;

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _filterQuery = string.Empty;

        public ObservableCollection<ScrobbleEntry> Entries { get; } = new();
        private ScrobbleEntry[] _allEntries = Array.Empty<ScrobbleEntry>();

        public ListeningHistoryViewModel(string historyPath) => _historyPath = historyPath;

        public async Task LoadAsync()
        {
            IsLoading = true;
            Entries.Clear();

            try
            {
                if (!File.Exists(_historyPath)) { IsLoading = false; return; }
                var json = await File.ReadAllTextAsync(_historyPath);
                var list = JsonSerializer.Deserialize<ScrobbleEntry[]>(json) ?? Array.Empty<ScrobbleEntry>();
                _allEntries = list;
                ApplyFilter();
            }
            catch { }
            finally { IsLoading = false; }
        }

        partial void OnFilterQueryChanged(string value) => ApplyFilter();

        private void ApplyFilter()
        {
            Entries.Clear();
            bool hasFilter = !string.IsNullOrWhiteSpace(FilterQuery);
            foreach (var entry in _allEntries)
            {
                if (!hasFilter ||
                    entry.Title.Contains(FilterQuery, StringComparison.OrdinalIgnoreCase) ||
                    entry.Artist.Contains(FilterQuery, StringComparison.OrdinalIgnoreCase))
                {
                    Entries.Add(entry);
                }
            }
        }

        [RelayCommand]
        private void Clear()
        {
            Entries.Clear();
            _allEntries = Array.Empty<ScrobbleEntry>();
            if (File.Exists(_historyPath)) File.Delete(_historyPath);
        }
    }
}
