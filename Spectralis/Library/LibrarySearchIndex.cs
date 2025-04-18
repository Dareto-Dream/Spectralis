using System.Collections.Generic;

namespace Spectralis.Library
{
    public class LibrarySearchIndex
    {
        private readonly LibraryRepository _repo;

        public LibrarySearchIndex(LibraryRepository repo)
        {
            _repo = repo;
        }

        public IList<LibraryTrack> Search(string query, int limit = 200)
        {
            if (string.IsNullOrWhiteSpace(query))
                return _repo.GetAll();

            var term = query.Trim();
            if (!term.EndsWith("*"))
                term = term + "*";

            return _repo.Search(term, limit);
        }

        public IList<LibraryTrack> SearchByArtist(string artist, int limit = 500)
        {
            return _repo.Search($"artist:{Escape(artist)}*", limit);
        }

        public IList<LibraryTrack> SearchByAlbum(string album, int limit = 500)
        {
            return _repo.Search($"album:{Escape(album)}*", limit);
        }

        private static string Escape(string s)
        {
            return s?.Replace("\"", "\"\"") ?? string.Empty;
        }
    }
}
