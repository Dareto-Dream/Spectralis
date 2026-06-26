namespace Spectralis.Core.Common;

public static class TimeFormat
{
    /// <summary>Formats seconds as m:ss, or h:mm:ss past the hour. Used by all timecode UI.</summary>
    public static string FormatSeconds(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
        {
            seconds = 0;
        }

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:D2}:{time.Seconds:D2}"
            : $"{time.Minutes}:{time.Seconds:D2}";
    }
}
