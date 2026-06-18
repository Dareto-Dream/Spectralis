using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Analysis;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.App.ViewModels
{
    public partial class BpmViewModel : ObservableObject
    {
        private readonly AnalysisWorker _worker;
        private readonly AnalysisCache _cache;
        private CancellationTokenSource? _cts;

        [ObservableProperty] private float _bpm;
        [ObservableProperty] private float _bpmConfidence;
        [ObservableProperty] private string _key = string.Empty;
        [ObservableProperty] private float _keyConfidence;
        [ObservableProperty] private float _loudnessLufs;
        [ObservableProperty] private bool _isAnalyzing;
        [ObservableProperty] private string _statusMessage = string.Empty;

        public BeatGrid? CurrentBeatGrid { get; private set; }

        public BpmViewModel(AnalysisWorker worker, AnalysisCache cache)
        {
            _worker = worker;
            _cache = cache;
        }

        public async Task LoadTrackAsync(string filePath)
        {
            if (filePath.StartsWith("streaming://", StringComparison.OrdinalIgnoreCase))
            {
                Bpm = 0f; Key = string.Empty; LoudnessLufs = 0f;
                StatusMessage = "Analysis not available for streaming tracks";
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            var cached = _cache.Get(filePath);
            if (cached != null)
            {
                Bpm = cached.Bpm;
                BpmConfidence = cached.BpmConfidence;
                Key = cached.KeyName;
                KeyConfidence = cached.KeyConfidence;
                LoudnessLufs = cached.LoudnessLufs;
                StatusMessage = "Loaded from cache";
                return;
            }

            IsAnalyzing = true;
            StatusMessage = "Analyzing…";

            try
            {
                var result = await _worker.AnalyzeAsync(filePath, _cts.Token);
                if (result == null) { StatusMessage = "Analysis failed"; return; }

                Bpm = result.Bpm.IsValid ? result.Bpm.Bpm : 0f;
                BpmConfidence = result.Bpm.Confidence;
                Key = result.Key.IsValid ? result.Key.Name : "Unknown";
                KeyConfidence = result.Key.Confidence;
                LoudnessLufs = result.LoudnessLufs;
                CurrentBeatGrid = result.BeatGrid;

                _cache.Store(result);
                await _cache.SaveAsync();
                StatusMessage = string.Empty;
            }
            catch (System.OperationCanceledException) { StatusMessage = string.Empty; }
            finally { IsAnalyzing = false; }
        }

        [RelayCommand]
        private async Task ReanalyzeAsync()
        {
            if (StatusMessage == "Analyzing…") return;
            _cache.Invalidate(StatusMessage);
            StatusMessage = string.Empty;
        }
    }
}
