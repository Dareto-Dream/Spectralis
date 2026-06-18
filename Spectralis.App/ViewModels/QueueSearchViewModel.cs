using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Queue;

namespace Spectralis.App.ViewModels
{
    public partial class QueueSearchViewModel : ObservableObject
    {
        private readonly PlayQueue _queue;

        [ObservableProperty] private string _query = string.Empty;
        [ObservableProperty] private ObservableCollection<PlayQueueItem> _results = new();
        [ObservableProperty] private bool _hasResults;

        public QueueSearchViewModel(PlayQueue queue)
        {
            _queue = queue;
            _queue.QueueChanged += (_, _) => { if (!string.IsNullOrEmpty(Query)) Search(); };
        }

        partial void OnQueryChanged(string value) => Search();

        private void Search()
        {
            Results.Clear();
            if (string.IsNullOrWhiteSpace(Query)) { HasResults = false; return; }

            var q = Query.Trim();
            foreach (var item in _queue.Items)
            {
                if (item.Track.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) == true ||
                    item.Track.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) == true ||
                    item.Track.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                {
                    Results.Add(item);
                }
            }
            HasResults = Results.Count > 0;
        }

        [RelayCommand]
        private void JumpTo(PlayQueueItem item)
        {
            int idx = _queue.Items.ToList().IndexOf(item);
            if (idx >= 0) _queue.PlayAt(idx);
        }

        [RelayCommand]
        private void Clear() => Query = string.Empty;
    }
}
