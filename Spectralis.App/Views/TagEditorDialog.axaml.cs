using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Spectralis.App.Views
{
    public partial class TagEditorDialog : Window
    {
        public TagEditorDialog()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
    }
}
