using CommunityToolkit.Mvvm.ComponentModel;
using Spectralis.Core.Library;

namespace Spectralis.App.ViewModels
{
    public partial class ScanProgressViewModel : ViewModelBase
    {
        [ObservableProperty] private int _scannedCount;
        [ObservableProperty] private int _totalEstimate;
        [ObservableProperty] private int _errorCount;
        [ObservableProperty] private string _currentFile = string.Empty;
        [ObservableProperty] private double _progressFraction;
        [ObservableProperty] private string _summary = string.Empty;
        [ObservableProperty] private bool _isScanning;

        public void Update(LibraryScanProgress progress)
        {
            ScannedCount = progress.ScannedCount;
            TotalEstimate = progress.TotalEstimate;
            ErrorCount = progress.ErrorCount;
            CurrentFile = System.IO.Path.GetFileName(progress.CurrentFile);
            ProgressFraction = progress.ProgressFraction;
            Summary = progress.Summary;
            IsScanning = !progress.IsComplete;
        }
    }
}
