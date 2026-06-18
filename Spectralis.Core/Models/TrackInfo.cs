using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Spectralis.Core.Models
{
    public partial class TrackInfo : ObservableObject
    {
        [ObservableProperty] private string _filePath = string.Empty;
        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string _artist = string.Empty;
        [ObservableProperty] private string _albumArtist = string.Empty;
        [ObservableProperty] private string _album = string.Empty;
        [ObservableProperty] private string _genre = string.Empty;
        [ObservableProperty] private int _year;
        [ObservableProperty] private int _trackNumber;
        [ObservableProperty] private int _discNumber;
        [ObservableProperty] private TimeSpan _duration;
        [ObservableProperty] private long _fileSizeBytes;
        [ObservableProperty] private int _bitrate;
        [ObservableProperty] private int _sampleRate;
        [ObservableProperty] private bool _isStreamed;
        [ObservableProperty] private string? _streamSource;
        [ObservableProperty] private string? _streamUrl;
        [ObservableProperty] private byte[]? _coverArtBytes;

        public bool HasCoverArt => _coverArtBytes != null && _coverArtBytes.Length > 0;

        public override string ToString() =>
            string.IsNullOrEmpty(_artist) ? _title : $"{_artist} — {_title}";
    }
}
