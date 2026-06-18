using System;
using Avalonia.Controls;
using Spectralis.App.Services;

namespace Spectralis.App.Controls
{
    public partial class AlbumWorldHost : UserControl
    {
        private AlbumWorldService? _service;

        public event EventHandler? CloseRequested;

        public AlbumWorldHost()
        {
            InitializeComponent();
            CloseButton.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        public void Load(AlbumWorldService service)
        {
            _service = service;
            if (service.Manifest != null)
                TitleBlock.Text = $"{service.Manifest.AlbumTitle} — {service.Manifest.Artist}";

            string? htmlPath = service.GetWorldHtmlPath();
            if (htmlPath != null)
                NavigateToWorld(htmlPath);
        }

        private void NavigateToWorld(string htmlPath)
        {
            // WebView2 integration — mounted in code so Avalonia designer doesn't choke
        }

        public void Unload()
        {
            _service = null;
            TitleBlock.Text = "Album World";
        }
    }
}
