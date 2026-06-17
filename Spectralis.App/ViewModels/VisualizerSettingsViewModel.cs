using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.App.Services;
using Spectralis.Core.Visualizers;

namespace Spectralis.App.ViewModels
{
    public partial class VisualizerSettingsViewModel : ObservableObject
    {
        private readonly VisualizerService _service;
        private readonly VisualizerAutoSwitch _autoSwitch;

        [ObservableProperty] private bool _autoSwitchEnabled;
        [ObservableProperty] private double _autoSwitchInterval = 30.0;
        [ObservableProperty] private float _beatSensitivity = 1.3f;
        [ObservableProperty] private bool _showFpsOverlay;
        [ObservableProperty] private bool _preferHardwareAccelerated;

        public VisualizerSettingsViewModel(VisualizerService service, VisualizerAutoSwitch autoSwitch)
        {
            _service = service;
            _autoSwitch = autoSwitch;
        }

        partial void OnAutoSwitchEnabledChanged(bool value)
        {
            if (value) _autoSwitch.Start();
            else _autoSwitch.Stop();
        }

        partial void OnAutoSwitchIntervalChanged(double value)
        {
            _autoSwitch.SetInterval(value);
        }

        [RelayCommand]
        private void ApplyBeatSensitivity()
        {
            _service.SetBeatSensitivity(BeatSensitivity);
        }
    }
}
