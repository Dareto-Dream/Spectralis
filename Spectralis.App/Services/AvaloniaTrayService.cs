using Avalonia.Controls;
using Avalonia.Platform;
using Spectralis.Core.Platform;

namespace Spectralis.App.Services;

public sealed class AvaloniaTrayService : ITrayService
{
    private readonly TrayIcon _trayIcon;
    private readonly NativeMenuItem _nowPlayingItem;
    private string _currentHeader = string.Empty;
    private string _currentTooltip = string.Empty;

    public event EventHandler? PlayMostRecentRequested;
    public event EventHandler? OpenRequested;
    public event EventHandler? ExitRequested;

    public AvaloniaTrayService()
    {
        _nowPlayingItem = new NativeMenuItem
        {
            Header = "Spectralis is idle",
            IsEnabled = false,
        };

        _trayIcon = new TrayIcon
        {
            Icon = LoadIcon(),
            ToolTipText = "Spectralis",
            IsVisible = false,
            Menu = BuildMenu(),
        };
        _trayIcon.Clicked += (_, _) => OpenRequested?.Invoke(this, EventArgs.Empty);
    }

    public void Show(string tooltip)
    {
        SetTooltip(string.IsNullOrWhiteSpace(tooltip) ? "Spectralis" : tooltip);
        _trayIcon.IsVisible = true;
    }

    public void UpdateNowPlaying(string title, string artist)
    {
        var header = string.IsNullOrWhiteSpace(title)
            ? "Spectralis is idle"
            : string.IsNullOrWhiteSpace(artist)
                ? title.Trim()
                : $"{artist.Trim()} - {title.Trim()}";

        if (!string.Equals(_currentHeader, header, StringComparison.Ordinal))
        {
            _currentHeader = header;
            _nowPlayingItem.Header = header;
        }

        SetTooltip($"Spectralis - {header}");
    }

    public void Hide()
    {
        _trayIcon.IsVisible = false;
    }

    public void Dispose()
    {
        _trayIcon.Dispose();
    }

    private NativeMenu BuildMenu()
    {
        var playMostRecent = CreateItem("Play most recent song", () => PlayMostRecentRequested?.Invoke(this, EventArgs.Empty));
        var exit = CreateItem("Close app", () => ExitRequested?.Invoke(this, EventArgs.Empty));

        return new NativeMenu
        {
            _nowPlayingItem,
            new NativeMenuItemSeparator(),
            playMostRecent,
            new NativeMenuItemSeparator(),
            exit,
        };
    }

    private void SetTooltip(string tooltip)
    {
        if (string.Equals(_currentTooltip, tooltip, StringComparison.Ordinal))
        {
            return;
        }

        _currentTooltip = tooltip;
        _trayIcon.ToolTipText = tooltip;
    }

    private static NativeMenuItem CreateItem(string header, Action onClick)
    {
        var item = new NativeMenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    private static WindowIcon? LoadIcon()
    {
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Spectralis.App/Assets/icon.png"));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            SpectralisLog.Warn($"Tray icon failed to load: {ex.Message}");
            return null;
        }
    }
}
