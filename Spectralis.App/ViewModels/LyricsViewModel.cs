using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.ViewModels
{
    public partial class LyricsViewModel : ObservableObject
    {
        private readonly LyricsAnnotationStore _store = new();
        private LyricsAnnotationFile? _annotations;
        private string? _currentTrackPath;

        [ObservableProperty] private string _currentLineText = string.Empty;
        [ObservableProperty] private string _currentWordText = string.Empty;
        [ObservableProperty] private LyricsAnnotation? _currentAnnotation;
        [ObservableProperty] private bool _hasLyrics;
        [ObservableProperty] private bool _showAnnotation;
        [ObservableProperty] private ObservableCollection<LyricsAnnotation> _annotations = new();

        private TimeSpan _currentTimestamp;

        public void OnLineChanged(object? sender, LrcLine line)
        {
            CurrentLineText = line.Text;
            CurrentWordText = string.Empty;
            _currentTimestamp = line.Timestamp;

            string key = FormatKey(line.Timestamp);
            if (_annotations != null && _annotations.Annotations.TryGetValue(key, out var ann))
            {
                CurrentAnnotation = ann;
                ShowAnnotation = true;
            }
            else
            {
                CurrentAnnotation = null;
                ShowAnnotation = false;
            }
        }

        public void OnWordHighlighted(object? sender, EnhancedWord word)
        {
            CurrentWordText = word.Text;
        }

        public async Task LoadAnnotationsAsync(string trackPath)
        {
            _currentTrackPath = trackPath;
            var file = await _store.LoadAsync(trackPath);
            _annotations = file ?? new LyricsAnnotationFile();
            RefreshAnnotationList();
        }

        [RelayCommand]
        private async Task AddAnnotationAsync(string explanation)
        {
            if (_currentTrackPath == null || string.IsNullOrWhiteSpace(explanation)) return;
            _annotations ??= new LyricsAnnotationFile();

            var ann = new LyricsAnnotation
            {
                TimestampKey = FormatKey(_currentTimestamp),
                Timestamp = _currentTimestamp,
                LineText = CurrentLineText,
                Explanation = explanation,
                CreatedAt = DateTimeOffset.UtcNow
            };
            _store.Upsert(_annotations, ann);
            await _store.SaveAsync(_currentTrackPath, _annotations);
            RefreshAnnotationList();
            CurrentAnnotation = ann;
            ShowAnnotation = true;
        }

        [RelayCommand]
        private async Task RemoveAnnotationAsync()
        {
            if (_currentTrackPath == null || _annotations == null) return;
            string key = FormatKey(_currentTimestamp);
            if (_store.Remove(_annotations, key))
            {
                await _store.SaveAsync(_currentTrackPath, _annotations);
                RefreshAnnotationList();
                CurrentAnnotation = null;
                ShowAnnotation = false;
            }
        }

        private void RefreshAnnotationList()
        {
            Annotations.Clear();
            if (_annotations == null) return;
            foreach (var a in _annotations.Annotations.Values)
                Annotations.Add(a);
        }

        private static string FormatKey(TimeSpan ts) =>
            $"{(int)ts.TotalMinutes:D2}:{ts.Seconds:D2}.{ts.Milliseconds:D3}";
    }
}
