using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

public partial class UpdateStatusWindow : Window
{
    public UpdateStatusWindow()
    {
        InitializeComponent();
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
