using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Spectralis.App.Converters;

/// <summary>Converts a "#RRGGBB"/"#AARRGGBB" hex string to a brush, falling back to a neutral swatch
/// when null/blank/unparsable (used for wheel-entry color previews before a palette color is picked).</summary>
public sealed class HexColorToBrushConverter : IValueConverter
{
    public static readonly HexColorToBrushConverter Instance = new();

    private static readonly IBrush Fallback = new SolidColorBrush(Color.FromRgb(90, 86, 94));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try { return new SolidColorBrush(Color.Parse(hex)); }
            catch { /* fall through to neutral swatch */ }
        }
        return Fallback;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
