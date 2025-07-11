using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Spectralis.Core.Audio;
using Spectralis.Core.Models;

namespace Spectralis.Core.Library
{
    public class LibraryScanner
    {
        private static readonly HashSet<string> _audioExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".mp3", ".flac", ".wav", ".ogg", ".aac", ".m4a", ".opus", ".wma", ".ape" };

        private readonly MetadataExtractor _extractor = new MetadataExtractor();
        private readonly LibraryRepository _repo;

        public event EventHandler<TrackInfo>? TrackScanned;
        public event EventHandler<string>? ScanError;

        public LibraryScanner(LibraryRepository repo) => _repo = repo;

        public async Task ScanFolderAsync(string folder, IProgress<string>? progress = null, CancellationToken ct = default)
        {
            if (!Directory.Exists(folder)) return;

            var files = Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (!_audioExts.Contains(Path.GetExtension(file))) continue;

                try
                {
                    progress?.Report(file);
                    var info = await _extractor.LoadMetadataAsync(file, ct);
                    await _repo.UpsertAsync(info, ct);
                    TrackScanned?.Invoke(this, info);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    ScanError?.Invoke(this, $"{file}: {ex.Message}");
                }
            }
        }
    }
}
