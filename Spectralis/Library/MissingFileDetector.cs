using System.Collections.Generic;
using System.IO;

namespace Spectralis.Library
{
    public class MissingFileDetector
    {
        private readonly LibraryRepository _repo;

        public MissingFileDetector(LibraryRepository repo)
        {
            _repo = repo;
        }

        public IList<LibraryTrack> FindMissing()
        {
            var all = _repo.GetAll();
            var missing = new List<LibraryTrack>();
            foreach (var track in all)
            {
                if (!File.Exists(track.Path))
                    missing.Add(track);
            }
            return missing;
        }

        public int RemoveMissing()
        {
            var missing = FindMissing();
            foreach (var track in missing)
                _repo.Delete(track.Path);
            return missing.Count;
        }
    }
}
