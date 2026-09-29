using System.Globalization;
using Avalonia.Data.Converters;
using Spectralis.Core.Capsule;

namespace Spectralis.App.Converters;

/// <summary>An unrevealed hidden achievement shows "???" instead of its real title/description —
/// both converters take the whole <see cref="AlbumAchievementEntry"/> (bind with no Path) since
/// the decision needs both <see cref="AlbumAchievementEntry.Hidden"/> and
/// <see cref="AlbumAchievementEntry.Unlocked"/> together, not just the field being displayed.</summary>
public sealed class AchievementTitleConverter : IValueConverter
{
    public static readonly AchievementTitleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AlbumAchievementEntry { Hidden: true, Unlocked: false } ? "???" : (value as AlbumAchievementEntry)?.Title;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}

public sealed class AchievementDescriptionConverter : IValueConverter
{
    public static readonly AchievementDescriptionConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is AlbumAchievementEntry { Hidden: true, Unlocked: false } ? "Undiscovered" : (value as AlbumAchievementEntry)?.Description;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.Data.BindingOperations.DoNothing;
}
