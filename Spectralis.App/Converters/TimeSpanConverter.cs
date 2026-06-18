using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Spectralis.App.Converters
{
    public class TimeSpanConverter : IValueConverter
    {
        public static readonly TimeSpanConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TimeSpan ts)
                return ts.TotalHours >= 1
                    ? ts.ToString(@"h\:mm\:ss")
                    : ts.ToString(@"m\:ss");
            return "0:00";
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class TimeSpanToSecondsConverter : IValueConverter
    {
        public static readonly TimeSpanToSecondsConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is TimeSpan ts ? ts.TotalSeconds : 0.0;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is double d ? TimeSpan.FromSeconds(d) : TimeSpan.Zero;
    }

    public class PlaybackProgressConverter : IMultiValueConverter
    {
        public static readonly PlaybackProgressConverter Instance = new();

        public object? Convert(System.Collections.Generic.IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] is TimeSpan pos && values[1] is TimeSpan dur)
                return dur.TotalSeconds > 0 ? pos.TotalSeconds / dur.TotalSeconds : 0.0;
            return 0.0;
        }
    }
}
