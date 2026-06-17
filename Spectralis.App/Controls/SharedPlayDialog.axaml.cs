using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Controls
{
    public partial class SharedPlayDialog : Window
    {
        public SharedPlayDialog()
        {
            InitializeComponent();
        }

        private void OnClose(object? sender, RoutedEventArgs e) => Close();
    }
}
