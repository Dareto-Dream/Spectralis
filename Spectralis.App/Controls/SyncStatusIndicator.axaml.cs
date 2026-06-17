using Avalonia.Controls;
using Avalonia.Media;

namespace Spectralis.App.Controls
{
    public partial class SyncStatusIndicator : UserControl
    {
        public SyncStatusIndicator()
        {
            InitializeComponent();
        }

        public void SetConnected(int listeners)
        {
            DotIndicator.Fill = Brushes.LimeGreen;
            StatusLabel.Text = $"Live · {listeners} listening";
            StatusLabel.Foreground = Brushes.LimeGreen;
        }

        public void SetDrifting()
        {
            DotIndicator.Fill = Brushes.Orange;
            StatusLabel.Text = "Syncing…";
            StatusLabel.Foreground = Brushes.Orange;
        }

        public void SetOffline()
        {
            DotIndicator.Fill = Brushes.Gray;
            StatusLabel.Text = "Offline";
            StatusLabel.Foreground = new SolidColorBrush(Colors.Gray);
        }
    }
}
