using System;
using Avalonia;
using Avalonia.Controls;
using Spectralis.Core.Audio;
using Spectralis.Core.Models;

namespace Spectralis.App.Services
{
    public class TrayService : IDisposable
    {
        private readonly IAudioEngine _engine;
        private readonly TrayIcon? _trayIcon;
        private bool _disposed;

        public event EventHandler? ShowRequested;

        public TrayService(IAudioEngine engine)
        {
            _engine = engine;

            if (!OperatingSystem.IsWindows()) return;

            _trayIcon = new TrayIcon();
            _trayIcon.ToolTipText = "Spectralis";
            _trayIcon.Clicked += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);

            var menu = new NativeMenu();

            var showItem = new NativeMenuItem("Show Spectralis");
            showItem.Click += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
            menu.Add(showItem);

            menu.Add(new NativeMenuItemSeparator());

            var playItem = new NativeMenuItem("Play / Pause");
            playItem.Click += (s, e) =>
            {
                if (_engine.State == PlaybackState.Playing) _engine.Pause();
                else _engine.Play();
            };
            menu.Add(playItem);

            var nextItem = new NativeMenuItem("Next Track");
            nextItem.Click += (s, e) => { };
            menu.Add(nextItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit");
            exitItem.Click += (s, e) => Application.Current?.Shutdown();
            menu.Add(exitItem);

            _trayIcon.Menu = menu;
        }

        public void UpdateTooltip(string text)
        {
            if (_trayIcon == null) return;
            _trayIcon.ToolTipText = text.Length > 63 ? text[..63] : text;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _trayIcon?.Dispose();
        }
    }
}
