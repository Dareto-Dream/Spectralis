using System.Text;

namespace Spectralis.Core.Lyrics;

public sealed class TimedLyricLine
{
    public TimedLyricLine(string text) => Text = text;

    public string Text { get; }

    /// <summary>Stamped start time in seconds, or null while untimed.</summary>
    public double? Timestamp { get; internal set; }

    /// <summary>Per-word stamps for word-mode tapping, parallel to Text.Split(' ', RemoveEmptyEntries)
    /// — index i is the i-th word's timestamp, or null if that word hasn't been tapped. Stays all-null
    /// for a line only ever tapped in Line Mode, which is what keeps ExportLrc's output plain for it.</summary>
    public double?[] WordTimestamps { get; internal set; } = [];
}

/// <summary>
/// The Timing Studio engine: load plain lyric lines, tap timestamps against
/// playback, adjust individual stamps, export an .lrc document.
/// </summary>
public sealed class LyricsTimingSession
{
    private readonly List<TimedLyricLine> _lines = [];

    public IReadOnlyList<TimedLyricLine> Lines => _lines;

    /// <summary>Index of the next line a tap will stamp; equals Lines.Count when done.</summary>
    public int CurrentIndex { get; private set; }

    public bool IsComplete => _lines.Count > 0 && CurrentIndex >= _lines.Count;

    public void LoadPlainText(string text)
    {
        _lines.Clear();
        CurrentIndex = 0;

        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length > 0)
            {
                var wordCount = line.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                _lines.Add(new TimedLyricLine(line) { WordTimestamps = new double?[wordCount] });
            }
        }
    }

    /// <summary>Stamps the current line at the playback position and advances. Returns the stamped index, or −1.</summary>
    public int Tap(double positionSeconds)
    {
        if (IsComplete || _lines.Count == 0)
        {
            return -1;
        }

        _lines[CurrentIndex].Timestamp = Math.Max(0, positionSeconds);
        return CurrentIndex++;
    }

    /// <summary>Moves the tap cursor back one line and clears its stamp (undo).</summary>
    public bool UndoLastTap()
    {
        if (CurrentIndex == 0)
        {
            return false;
        }

        CurrentIndex--;
        _lines[CurrentIndex].Timestamp = null;
        return true;
    }

    public void AdjustTimestamp(int index, double seconds)
    {
        if (index >= 0 && index < _lines.Count && _lines[index].Timestamp is not null)
        {
            _lines[index].Timestamp = Math.Max(0, seconds);
        }
    }

    /// <summary>Directly stamps a line by index, regardless of the tap cursor. Used by word-mode tapping.</summary>
    public void StampLine(int index, double seconds)
    {
        if (index < 0 || index >= _lines.Count)
        {
            return;
        }

        _lines[index].Timestamp = Math.Max(0, seconds);
        if (index >= CurrentIndex)
        {
            CurrentIndex = index + 1;
        }
    }

    /// <summary>Clears a single line's stamp by index. Used when undoing a word-mode tap.</summary>
    public void ClearLine(int index)
    {
        if (index < 0 || index >= _lines.Count)
        {
            return;
        }

        _lines[index].Timestamp = null;
        if (CurrentIndex > index)
        {
            CurrentIndex = index;
        }
    }

    /// <summary>Stamps one word within a line for word-mode tapping — this is what actually
    /// backs the exported enhanced-LRC word tags, on top of stamping the line itself (matching
    /// StampLine's convention) if this is the line's first word.</summary>
    public void StampWord(int lineIndex, int wordIndex, double seconds)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count)
        {
            return;
        }

        var line = _lines[lineIndex];
        var clamped = Math.Max(0, seconds);
        if (wordIndex >= 0 && wordIndex < line.WordTimestamps.Length)
        {
            line.WordTimestamps[wordIndex] = clamped;
        }

        if (line.Timestamp is null)
        {
            line.Timestamp = clamped;
            if (lineIndex >= CurrentIndex)
            {
                CurrentIndex = lineIndex + 1;
            }
        }
    }

    /// <summary>Clears one word's stamp (word-mode undo). Also clears the line's own stamp when
    /// undoing its first word, mirroring StampWord stamping the line on that same word.</summary>
    public void ClearWord(int lineIndex, int wordIndex)
    {
        if (lineIndex < 0 || lineIndex >= _lines.Count)
        {
            return;
        }

        var line = _lines[lineIndex];
        if (wordIndex >= 0 && wordIndex < line.WordTimestamps.Length)
        {
            line.WordTimestamps[wordIndex] = null;
        }

        if (wordIndex == 0)
        {
            line.Timestamp = null;
            if (CurrentIndex > lineIndex)
            {
                CurrentIndex = lineIndex;
            }
        }
    }

    public void Reset()
    {
        foreach (var line in _lines)
        {
            line.Timestamp = null;
            Array.Clear(line.WordTimestamps);
        }

        CurrentIndex = 0;
    }

    /// <summary>Exports stamped lines as LRC text. Untimed trailing lines are omitted. A line
    /// with any word-mode stamps gets enhanced-LRC &lt;mm:ss.xx&gt; word tags inline; a line only
    /// ever tapped in Line Mode exports as plain text, same as before word tags existed.</summary>
    public string ExportLrc(string? title = null, string? artist = null)
    {
        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(title))
        {
            builder.Append("[ti:").Append(title.Trim()).AppendLine("]");
        }

        if (!string.IsNullOrWhiteSpace(artist))
        {
            builder.Append("[ar:").Append(artist.Trim()).AppendLine("]");
        }

        foreach (var line in _lines.Where(static line => line.Timestamp is not null)
                                   .OrderBy(static line => line.Timestamp))
        {
            builder.Append(FormatTimestamp(line.Timestamp!.Value)).AppendLine(BuildLineBody(line));
        }

        return builder.ToString();
    }

    private static string BuildLineBody(TimedLyricLine line)
    {
        if (line.WordTimestamps.Length == 0 || line.WordTimestamps.All(static t => t is null))
        {
            return line.Text;
        }

        var words = line.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        for (var i = 0; i < words.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            if (i < line.WordTimestamps.Length && line.WordTimestamps[i] is { } wordTime)
            {
                builder.Append(FormatWordTimestamp(wordTime));
            }

            builder.Append(words[i]);
        }

        return builder.ToString();
    }

    /// <summary>Writes the export as an .lrc sidecar next to the audio file. Returns the path.</summary>
    public string SaveSidecar(string audioPath, string? title = null, string? artist = null)
    {
        var lrcPath = Path.ChangeExtension(audioPath, ".lrc");
        File.WriteAllText(lrcPath, ExportLrc(title, artist));
        return lrcPath;
    }

    public static string FormatTimestamp(double seconds) => $"[{FormatTimeCode(seconds)}]";

    private static string FormatWordTimestamp(double seconds) => $"<{FormatTimeCode(seconds)}>";

    private static string FormatTimeCode(double seconds)
    {
        var totalCentiseconds = (long)Math.Round(seconds * 100, MidpointRounding.AwayFromZero);
        var minutes = totalCentiseconds / 6000;
        var secs = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;
        return $"{minutes:D2}:{secs:D2}.{centiseconds:D2}";
    }
}
