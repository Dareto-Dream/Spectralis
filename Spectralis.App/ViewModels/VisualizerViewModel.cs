using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.ViewModels
{
    public partial class VisualizerViewModel : ViewModelBase
    {
        private readonly VisualizerRegistry _registry;

        [ObservableProperty] private ObservableCollection<VisualizerInfo> _available = new();
        [ObservableProperty] private VisualizerInfo? _selected;
        [ObservableProperty] private bool _isFullscreen;

        public VisualizerViewModel(VisualizerRegistry registry)
        {
            _registry = registry;
            foreach (var info in _registry.All.Values) Available.Add(info);
        }

        [RelayCommand]
        private void SelectById(string id)
        {
            foreach (var info in Available)
                if (info.Id == id) { Selected = info; break; }
        }

        [RelayCommand]
        private void ToggleFullscreen() => IsFullscreen = !IsFullscreen;

        partial void OnSelectedChanged(VisualizerInfo? value) =>
            VisualizerChanged?.Invoke(this, value);

        public event System.EventHandler<VisualizerInfo?>? VisualizerChanged;
    }
}
