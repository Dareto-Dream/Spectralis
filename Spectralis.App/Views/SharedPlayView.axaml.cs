using Avalonia.Controls;
using Avalonia.Input.Platform;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class SharedPlayView : UserControl
{
    public SharedPlayView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private SharedPlayViewModel? _vm;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null)
            _vm.CopyToClipboardRequested -= OnCopyToClipboard;

        _vm = DataContext as SharedPlayViewModel;

        if (_vm is not null)
            _vm.CopyToClipboardRequested += OnCopyToClipboard;
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
}
