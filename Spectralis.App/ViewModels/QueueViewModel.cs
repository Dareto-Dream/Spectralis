using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Queue;

namespace Spectralis.App.ViewModels
{
    public partial class QueueViewModel : ViewModelBase
    {
        private readonly ServiceContainer _services;

        [ObservableProperty] private ObservableCollection<PlayQueueItem> _items = new();
        [ObservableProperty] private ObservableCollection<PlayQueueItem> _filteredItems = new();
        [ObservableProperty] private PlayQueueItem? _selectedItem;
        [ObservableProperty] private int _currentIndex;
        [ObservableProperty] private bool _isShuffle;
        [ObservableProperty] private Core.Queue.RepeatMode _repeatMode;
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private int _dragFromIndex = -1;

        public int TotalCount => _items.Count;
        public TimeSpan TotalDuration => TimeSpan.FromSeconds(_items.Sum(i => i.Track.Duration.TotalSeconds));

        public QueueViewModel(ServiceContainer services)
        {
            _services = services;
            _services.Queue.QueueChanged += (s, e) => RefreshItems();
            _services.Queue.CurrentChanged += (s, item) =>
            {
                SelectedItem = item;
                CurrentIndex = _services.Queue.CurrentIndex;
            };
        }

        partial void OnSearchTextChanged(string value) => ApplyFilter();

        private void RefreshItems()
        {
            Items.Clear();
            foreach (var item in _services.Queue.Items) Items.Add(item);
            ApplyFilter();
            OnPropertyChanged(nameof(TotalCount));
            OnPropertyChanged(nameof(TotalDuration));
        }

        private void ApplyFilter()
        {
            FilteredItems.Clear();
            var query = SearchText?.Trim() ?? string.Empty;
            foreach (var item in Items)
            {
                if (string.IsNullOrEmpty(query) ||
                    (item.Track.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true) ||
                    (item.Track.Artist?.Contains(query, StringComparison.OrdinalIgnoreCase) == true))
                {
                    FilteredItems.Add(item);
                }
            }
        }

        [RelayCommand]
        private void DragMove(int[] fromTo)
        {
            if (fromTo == null || fromTo.Length < 2) return;
            int from = fromTo[0], to = fromTo[1];
            if (from >= 0 && from < _services.Queue.Count &&
                to >= 0 && to < _services.Queue.Count)
            {
                _services.Queue.Move(from, to);
            }
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (SelectedItem == null) return;
            int idx = Items.IndexOf(SelectedItem);
            if (idx >= 0) _services.Queue.Remove(_services.Queue.Items[idx]);
        }

        [RelayCommand]
        private void MoveSelectedUp()
        {
            if (SelectedItem == null) return;
            int idx = Items.IndexOf(SelectedItem);
            if (idx > 0) _services.Queue.Move(idx, idx - 1);
        }

        [RelayCommand]
        private void MoveSelectedDown()
        {
            if (SelectedItem == null) return;
            int idx = Items.IndexOf(SelectedItem);
            if (idx >= 0 && idx < _services.Queue.Count - 1) _services.Queue.Move(idx, idx + 1);
        }

        [RelayCommand]
        private void Clear() => _services.Queue.Clear();

        [RelayCommand]
        private void PlaySelected()
        {
            if (SelectedItem == null) return;
            int idx = Items.IndexOf(SelectedItem);
            if (idx >= 0) _services.Queue.PlayAt(idx);
        }

        [RelayCommand]
        private void ClearSearch() => SearchText = string.Empty;

        [RelayCommand]
        private void ToggleShuffle()
        {
            _services.Queue.SetShuffle(!_services.Queue.IsShuffled);
            IsShuffle = _services.Queue.IsShuffled;
        }

        [RelayCommand]
        private void CycleRepeat()
        {
            var next = RepeatMode switch
            {
                Core.Queue.RepeatMode.None => Core.Queue.RepeatMode.RepeatAll,
                Core.Queue.RepeatMode.RepeatAll => Core.Queue.RepeatMode.RepeatOne,
                _ => Core.Queue.RepeatMode.None
            };
            _services.Queue.RepeatMode = next;
            RepeatMode = next;
        }
    }
}
