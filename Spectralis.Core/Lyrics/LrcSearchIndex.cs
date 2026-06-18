using System;
using System.Collections.Generic;

namespace Spectralis.Core.Lyrics
{
    public class LrcSearchIndex
    {
        private readonly List<(LrcLine Line, int LineIndex)> _entries = new();

        public void Build(LrcFile file)
        {
            _entries.Clear();
            for (int i = 0; i < file.Lines.Count; i++)
                _entries.Add((file.Lines[i], i));
        }

        public IReadOnlyList<(LrcLine Line, int LineIndex)> Search(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return _entries;
            var results = new List<(LrcLine, int)>();
            foreach (var entry in _entries)
            {
                if (entry.Line.Text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    results.Add(entry);
            }
            return results;
        }

        public void Clear() => _entries.Clear();
    }
}
