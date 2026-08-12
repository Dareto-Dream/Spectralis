using Spectralis.App.Services;

namespace Spectralis.App.ViewModels;

/// <summary>Nav identity for the Song Wars sidebar page. Song Wars is pure
/// code-behind (SongWarsView owns all state) — this only exists so the page has
/// a stable ViewModelBase for the sidebar/ViewLocator machinery and a settings
/// reference for the empty-state OLED binding.</summary>
public sealed class SongWarsViewModel(AppSettings settings) : ViewModelBase
{
    public bool IsOledTheme => settings.ThemeMode == AppThemeMode.Oled;
}
