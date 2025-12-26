using Avalonia.Controls;
using Avalonia.Input;

namespace Spectralis.App.Views
{
    public partial class StreamingSearchView : UserControl
    {
        public StreamingSearchView()
        {
            InitializeComponent();
        }

        private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is ViewModels.StreamingViewModel vm)
                vm.SearchCommand.Execute(null);
        }
    }
}
