using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views;

/// <summary>Themed single-button message box.</summary>
public partial class MessageWindow : Window
{
    public MessageWindow()
    {
        InitializeComponent();
    }

    public static async Task ShowAsync(Window owner, string title, string message)
    {
        var window = new MessageWindow { Title = title };
        window.MessageText.Text = message;
        await window.ShowDialog(owner);
    }

    private void OnOk(object? sender, RoutedEventArgs e) => Close();
}
