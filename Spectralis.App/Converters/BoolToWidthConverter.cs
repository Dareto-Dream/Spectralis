using System.Globalization;
using Avalonia.Data.Converters;

namespace Spectralis.App.Converters;

/// <summary>
/// Maps a bool to one of two widths supplied as "trueWidth|falseWidth" in the
/// converter parameter. Used to widen the effects sidebar when the parametric EQ
/// (which needs room for its curve editor) is the selected rack slot.
/// </summary>
public sealed class BoolToWidthConverter : IValueConverter
{
    public static readonly BoolToWidthConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "300|300").Split('|');
        var on = double.TryParse(parts.ElementAtOrDefault(0), NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : 300;
        var off = double.TryParse(parts.ElementAtOrDefault(1), NumberStyles.Any, CultureInfo.InvariantCulture, out var f) ? f : 300;
        return value is true ? on : off;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}
