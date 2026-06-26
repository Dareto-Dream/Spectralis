using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class DevToolsWindow : Window
{
    public DevToolsWindow()
    {
        InitializeComponent();
    }

    private async void OnCopyDiagnostics(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            vm.MarkDiagnosticsCopyFailed("clipboard is not available.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(vm.BuildDiagnosticsText());
            vm.MarkDiagnosticsCopied();
        }
        catch (Exception ex)
        {
            vm.MarkDiagnosticsCopyFailed(ex.Message);
        }
    }

    private void OnClearRemoteCache(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var freed = RemoteAudioCache.ClearAll();
        var remaining = RemoteAudioCache.GetCacheSizeBytes();
        vm.SetCacheClearedStatus(freed, remaining);
    }

    private void OnApplyCustomObsJson(object? sender, RoutedEventArgs e) =>
        (DataContext as SettingsViewModel)?.ObsEditor.ApplyCustomObsJson();

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
