using Avalonia.Controls;

namespace Spectralis.App.Controls
{
    public partial class NowPlayingMiniplayer : UserControl
    {
        public NowPlayingMiniplayer()
        {
            InitializeComponent();
        }

        public void Update(string title, string artist)
        {
            TitleText.Text = title;
            ArtistText.Text = artist;
        }
    }
}
