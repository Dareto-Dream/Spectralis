using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Spectralis.Library
{
    public class RescanResult
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Removed { get; set; }
        public IList<string> Errors { get; set; } = new List<string>();
    }

    public class LibraryRescanService
    {
        private readonly LibraryDb _db;
        private readonly LibraryRepository _repo;
        private readonly LibraryScanner _scanner;
        private readonly MissingFileDetector _missingDetector;

        public event EventHandler<ScanProgress> Progress;

        public LibraryRescanService(LibraryDb db, LibraryRepository repo, LibraryScanner scanner)
        {
            _db = db;
            _repo = repo;
            _scanner = scanner;
            _missingDetector = new MissingFileDetector(repo);
        }

        public async Task<RescanResult> RescanAsync(IEnumerable<string> folders, CancellationToken ct = default)
        {
            var result = new RescanResult();

            _scanner.Progress += (s, p) =>
            {
                Progress?.Invoke(this, p);
            };

            int removed = _missingDetector.RemoveMissing();
            result.Removed = removed;

            var countBefore = _repo.GetAll().Count;

            await _scanner.ScanAsync(folders, ct);

            var countAfter = _repo.GetAll().Count;
            result.Added = Math.Max(0, countAfter - countBefore + removed);
            result.Updated = Math.Max(0, countBefore - removed - (countAfter - result.Added));

            return result;
        }
    }
}
