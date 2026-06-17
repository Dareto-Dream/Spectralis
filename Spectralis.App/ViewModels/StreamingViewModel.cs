using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Audio;
using Spectralis.Core.Queue;
using Spectralis.Core.Streaming;

namespace Spectralis.App.ViewModels
{
    public partial class StreamingViewModel : ObservableObject
    {
        private readonly StreamingRegistry _streaming;
        private readonly PlayQueue _queue;
        private readonly IAudioEngine _engine;
        private CancellationTokenSource? _cts;

        [ObservableProperty] private string _searchQuery = string.Empty;
        [ObservableProperty] private string _selectedSource = string.Empty;
        [ObservableProperty] private StreamingResultViewModel? _selectedResult;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _statusMessage = string.Empty;

        public ObservableCollection<StreamingResultViewModel> Results { get; } = new();
        public ObservableCollection<string> AvailableSources { get; } = new();

        public StreamingViewModel(StreamingRegistry streaming, PlayQueue queue, IAudioEngine engine)
        {
            _streaming = streaming;
            _queue = queue;
            _engine = engine;

            foreach (var src in streaming.GetAll()) AvailableSources.Add(src.Id);
            if (AvailableSources.Count > 0) SelectedSource = AvailableSources[0];
        }

        [RelayCommand]
        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchQuery)) return;
            if (string.IsNullOrEmpty(SelectedSource)) { StatusMessage = "No streaming source available."; return; }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            IsLoading = true;
            StatusMessage = string.Empty;
            Results.Clear();

            try
            {
                var results = await _streaming.SearchAsync(SelectedSource, SearchQuery, limit: 25, _cts.Token);
                foreach (var track in results)
                    Results.Add(new StreamingResultViewModel(track));

                StatusMessage = Results.Count == 0 ? "No results." : string.Empty;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { StatusMessage = $"Search failed: {ex.Message}"; }
            finally { IsLoading = false; }
        }

        [RelayCommand]
        private async Task PlayNowAsync(StreamingResultViewModel? item)
        {
            if (item == null) return;
            StatusMessage = "Opening stream…";
            try
            {
                var stream = await _streaming.OpenStreamAsync(item.Track.SourceId, item.Track.Id);
                if (stream == null) { StatusMessage = "Stream unavailable"; return; }
                await _engine.LoadStreamAsync(stream, item.Track.ToTrackInfo());
                await _engine.PlayAsync();
                StatusMessage = string.Empty;
            }
            catch (Exception ex) { StatusMessage = ex.Message; }
        }

        [RelayCommand]
        private void Enqueue(StreamingResultViewModel? item)
        {
            if (item == null) return;
            _queue.Enqueue(new PlayQueueItem { Track = item.Track.ToTrackInfo() });
        }
    }
}
