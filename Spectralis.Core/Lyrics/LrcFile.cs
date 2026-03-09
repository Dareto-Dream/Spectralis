using System;
using System.Collections.Generic;

namespace Spectralis.Core.Lyrics
{
    public class LrcFile
    {
        public Dictionary<string, string> Metadata { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<LrcLine> Lines { get; } = new();

        public string? Title => Metadata.TryGetValue("ti", out var v) ? v : null;
        public string? Artist => Metadata.TryGetValue("ar", out var v) ? v : null;
        public string? Album => Metadata.TryGetValue("al", out var v) ? v : null;
        public TimeSpan Offset => Metadata.TryGetValue("offset", out var v) &&
            double.TryParse(v, out double ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero;

        public LrcLine? GetCurrentLine(TimeSpan position)
        {
            if (Lines.Count == 0) return null;
            var adjusted = position - Offset;
            LrcLine? current = null;
            foreach (var line in Lines)
            {
                if (line.Timestamp <= adjusted) current = line;
                else break;
            }
            return current;
        }

        public int GetCurrentLineIndex(TimeSpan position)
        {
            if (Lines.Count == 0) return -1;
            var adjusted = position - Offset;
            int idx = -1;
            for (int i = 0; i < Lines.Count; i++)
            {
                if (Lines[i].Timestamp < adjusted) idx = i;
                else break;
            }
            return idx;
        }
    }

    public class LrcLine
    {
        public TimeSpan Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
        public List<EnhancedWord>? Words { get; init; }
        public bool IsEnhanced => Words != null && Words.Count > 0;
    }

    public class EnhancedWord
    {
        public TimeSpan Timestamp { get; init; }
        public string Text { get; init; } = string.Empty;
    }
}
