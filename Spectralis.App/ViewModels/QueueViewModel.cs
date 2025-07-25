using System.Collections.ObjectModel;
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
        [ObservableProperty] private PlayQueueItem? _selectedItem;
        [ObservableProperty] private int _currentIndex;
        [ObservableProperty] private bool _isShuffle;
        [ObservableProperty] private Core.Models.RepeatMode _repeatMode;

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

        private void RefreshItems()
        {
            Items.Clear();
            foreach (var item in _services.Queue.Items) Items.Add(item);
        }

        [RelayCommand]
        private void RemoveSelected()
        {
            if (SelectedItem == null) return;
            int idx = _services.Queue.IndexOf(SelectedItem);
            if (idx >= 0) _services.Queue.RemoveAt(idx);
        }

        [RelayCommand]
        private void MoveSelectedUp()
        {
            if (SelectedItem == null) return;
            int idx = _services.Queue.IndexOf(SelectedItem);
            if (idx > 0) _services.Queue.Move(idx, idx - 1);
        }

        [RelayCommand]
        private void MoveSelectedDown()
        {
            if (SelectedItem == null) return;
            int idx = _services.Queue.IndexOf(SelectedItem);
            if (idx >= 0 && idx < _services.Queue.Count - 1) _services.Queue.Move(idx, idx + 1);
        }

        [RelayCommand]
        private void Clear() => _services.Queue.Clear();

        [RelayCommand]
        private void PlaySelected()
        {
            if (SelectedItem == null) return;
            int idx = _services.Queue.IndexOf(SelectedItem);
            if (idx >= 0) _services.Queue.PlayAt(idx);
        }
    }
}
