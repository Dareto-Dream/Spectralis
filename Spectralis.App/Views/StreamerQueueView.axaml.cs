using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class StreamerQueueView : UserControl
{
    public StreamerQueueView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private StreamerQueueViewModel? _vm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.CopyToClipboardRequested -= OnCopyToClipboard;
            _vm.OpenUrlRequested -= OnOpenUrl;
            _vm.SettingsSaveRequested -= OnSettingsSave;
        }

        _vm = DataContext as StreamerQueueViewModel;

        if (_vm is not null)
        {
            _vm.CopyToClipboardRequested += OnCopyToClipboard;
            _vm.OpenUrlRequested += OnOpenUrl;
            _vm.SettingsSaveRequested += OnSettingsSave;
        }
    }

    private async void OnCopyToClipboard(string text)
    {
        try
        {
            if (TopLevel.GetTopLevel(this)?.Clipboard is IClipboard clipboard)
                await clipboard.SetTextAsync(text);
        }
        catch { }
    }

    private void OnOpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { }
    }

    private void OnSettingsSave(AppSettings settings)
    {
        AppSettingsStore.Save(settings);
    }
}
