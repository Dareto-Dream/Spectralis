using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Spectralis.Core.Lyrics;

namespace Spectralis.App.ViewModels
{
    public partial class TimingStudioViewModel : ObservableObject
    {
        private readonly LrcParser _parser = new();
        private readonly LyricsLoader _loader = new();
        private Func<TimeSpan>? _positionGetter;

        [ObservableProperty] private string _rawLrcText = string.Empty;
        [ObservableProperty] private string _statusText = "No file loaded";
        [ObservableProperty] private int _selectedLineIndex = -1;
        [ObservableProperty] private bool _isDirty;

        public ObservableCollection<TimingStudioLine> Lines { get; } = new();

        public void SetPositionGetter(Func<TimeSpan> getter) => _positionGetter = getter;

        [RelayCommand]
        private void ParseRaw()
        {
            if (string.IsNullOrWhiteSpace(RawLrcText)) return;
            var lrc = _parser.Parse(RawLrcText);
            Lines.Clear();
            foreach (var line in lrc.Lines)
                Lines.Add(new TimingStudioLine { Timestamp = line.Timestamp, Text = line.Text });
            StatusText = $"Parsed {Lines.Count} lines";
            IsDirty = false;
        }

        [RelayCommand]
        private void StampCurrentLine()
        {
            if (_positionGetter == null) return;
            int idx = SelectedLineIndex >= 0 ? SelectedLineIndex : FindNextUnstamped();
            if (idx < 0 || idx >= Lines.Count) return;

            var pos = _positionGetter();
            Lines[idx].Timestamp = pos;
            Lines[idx].IsManuallyStamped = true;
            IsDirty = true;

            if (idx + 1 < Lines.Count)
                SelectedLineIndex = idx + 1;
        }

        [RelayCommand]
        private void NudgeTimestamp(double ms)
        {
            if (SelectedLineIndex < 0 || SelectedLineIndex >= Lines.Count) return;
            var line = Lines[SelectedLineIndex];
            var nudge = TimeSpan.FromMilliseconds(ms);
            var newTs = line.Timestamp + nudge;
            if (newTs < TimeSpan.Zero) newTs = TimeSpan.Zero;
            line.Timestamp = newTs;
            IsDirty = true;
        }

        [RelayCommand]
        private async Task ExportAsync(string outputPath)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var line in Lines)
            {
                int min = (int)line.Timestamp.TotalMinutes;
                int sec = line.Timestamp.Seconds;
                int ms = line.Timestamp.Milliseconds / 10;
                sb.AppendLine($"[{min:D2}:{sec:D2}.{ms:D2}]{line.Text}");
            }
            await System.IO.File.WriteAllTextAsync(outputPath, sb.ToString());
            IsDirty = false;
            StatusText = $"Exported to {System.IO.Path.GetFileName(outputPath)}";
        }

        private int FindNextUnstamped()
        {
            for (int i = 0; i < Lines.Count; i++)
                if (!Lines[i].IsManuallyStamped) return i;
            return -1;
        }
    }

    public partial class TimingStudioLine : ObservableObject
    {
        [ObservableProperty] private TimeSpan _timestamp;
        [ObservableProperty] private string _text = string.Empty;
        [ObservableProperty] private bool _isManuallyStamped;

        public string DisplayTimestamp =>
            $"[{(int)Timestamp.TotalMinutes:D2}:{Timestamp.Seconds:D2}.{Timestamp.Milliseconds / 10:D2}]";
    }
}
