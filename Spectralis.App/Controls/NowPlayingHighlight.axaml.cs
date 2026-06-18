using Avalonia.Controls;

namespace Spectralis.App.Controls
{
    public partial class NowPlayingHighlight : UserControl
    {
        public NowPlayingHighlight() => InitializeComponent();
        private void InitializeComponent() => Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(this);
    }
}
