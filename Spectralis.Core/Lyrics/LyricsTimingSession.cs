using System.Text;

namespace Spectralis.Core.Lyrics;

public sealed class TimedLyricLine
{
    public TimedLyricLine(string text) => Text = text;

    public string Text { get; }

    /// <summary>Stamped start time in seconds, or null while untimed.</summary>
    public double? Timestamp { get; internal set; }
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
                _lines.Add(new TimedLyricLine(line));
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

    public void Reset()
    {
        foreach (var line in _lines)
        {
            line.Timestamp = null;
        }

        CurrentIndex = 0;
    }

    /// <summary>Exports stamped lines as LRC text. Untimed trailing lines are omitted.</summary>
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
            builder.Append(FormatTimestamp(line.Timestamp!.Value)).AppendLine(line.Text);
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

    public static string FormatTimestamp(double seconds)
    {
        var totalCentiseconds = (long)Math.Round(seconds * 100, MidpointRounding.AwayFromZero);
        var minutes = totalCentiseconds / 6000;
        var secs = (totalCentiseconds / 100) % 60;
        var centiseconds = totalCentiseconds % 100;
        return $"[{minutes:D2}:{secs:D2}.{centiseconds:D2}]";
    }
}
