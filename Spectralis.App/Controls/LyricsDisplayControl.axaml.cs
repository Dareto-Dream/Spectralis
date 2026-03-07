using Avalonia.Controls;
using Spectralis.App.ViewModels;

namespace Spectralis.App.Controls
{
    public partial class LyricsDisplayControl : UserControl
    {
        public LyricsDisplayControl()
        {
            InitializeComponent();
        }

        public void SetViewModel(LyricsViewModel vm)
        {
            DataContext = vm;
        }
    }
}
