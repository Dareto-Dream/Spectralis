using Avalonia.Controls;
using Avalonia.Interactivity;
using Spectralis.App.Services;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Views;

public partial class WidgetTesterView : UserControl
{
    public WidgetTesterView()
    {
        InitializeComponent();
        DataContext = new WidgetTesterViewModel();
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WidgetTesterViewModel vm
            || (sender as Control)?.DataContext is not DevWidget widget
            || TopLevel.GetTopLevel(this) is not Window owner)
        {
            return;
        }

        await vm.SpawnAsync(widget, owner);
    }
}
