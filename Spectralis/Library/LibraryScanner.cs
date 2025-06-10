using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Audio;

namespace Spectralis.Library
{
    public class ScanProgress
    {
        public int Total { get; set; }
        public int Processed { get; set; }
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public string CurrentFile { get; set; }
    }

    public class LibraryScanner
    {
        private readonly LibraryRepository _repo;
        private CancellationTokenSource _cts;

        public event EventHandler<ScanProgress> Progress;
        public event EventHandler<string> ScanError;
        public event EventHandler ScanComplete;

        public bool IsScanning { get; private set; }

        public LibraryScanner(LibraryRepository repo)
        {
            _repo = repo;
        }

        public void ScanAsync(IEnumerable<string> folders)
        {
            if (IsScanning) return;
            _cts = new CancellationTokenSource();
            IsScanning = true;

            Task.Run(() =>
            {
                try { Scan(folders, _cts.Token); }
                finally { IsScanning = false; ScanComplete?.Invoke(this, EventArgs.Empty); }
            });
        }

        public void Cancel() => _cts?.Cancel();

        private void Scan(IEnumerable<string> folders, CancellationToken ct)
        {
            var files = new List<string>();
            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                foreach (var ext in FormatDetector.SupportedExtensions)
                    files.AddRange(Directory.GetFiles(folder, $"*{ext}", SearchOption.AllDirectories));
            }

            var progress = new ScanProgress { Total = files.Count };

            foreach (var file in files)
            {
                if (ct.IsCancellationRequested) break;

                progress.CurrentFile = Path.GetFileName(file);
                progress.Processed++;
                Progress?.Invoke(this, progress);

                try
                {
                    bool exists = _repo.Exists(file);
                    var info = MetadataExtractor.Extract(file);
                    var track = new LibraryTrack
                    {
                        Path = file,
                        Title = info.Title,
                        Artist = info.Artist,
                        Album = info.Album,
                        Genre = info.Genre,
                        Year = info.Year,
                        TrackNumber = (int)info.TrackNumber,
                        DurationMs = (long)info.Duration.TotalMilliseconds,
                        Bitrate = info.Bitrate,
                        SampleRate = info.SampleRate,
                        Channels = info.Channels,
                        Format = info.Format
                    };
                    _repo.Upsert(track);
                    if (exists) progress.Updated++;
                    else progress.Added++;
                }
                catch (Exception ex)
                {
                    progress.Skipped++;
                    ScanError?.Invoke(this, $"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
    }
}
