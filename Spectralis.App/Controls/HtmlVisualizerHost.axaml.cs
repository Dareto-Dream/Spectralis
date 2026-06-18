using Avalonia.Controls;
using Spectralis.Core.Audio;

namespace Spectralis.App.Controls
{
    public partial class HtmlVisualizerHost : UserControl
    {
        private string? _currentHtmlPath;

        public HtmlVisualizerHost()
        {
            InitializeComponent();
        }

        public void LoadHtml(string htmlPath)
        {
            _currentHtmlPath = htmlPath;
            // WebView2 integration pending — see TODO in A4 milestone
        }

        public void PostFrame(in AudioFrame frame)
        {
            if (_currentHtmlPath == null) return;
            // will post JSON message to WebView2 via ExecuteScriptAsync in A4
        }

        public void Unload()
        {
            _currentHtmlPath = null;
        }
    }
}
