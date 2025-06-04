using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Audio;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class SystemTrayManager : IDisposable
    {
        private readonly NotifyIcon _icon;
        private readonly AudioEngine _engine;
        private readonly PlayQueue _queue;
        private bool _disposed;

        public event EventHandler ShowRequested;

        public SystemTrayManager(AudioEngine engine, PlayQueue queue)
        {
            _engine = engine;
            _queue = queue;

            _icon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Visible = true,
                Text = "Spectralis"
            };

            var menu = new ContextMenuStrip();
            var miShow = new ToolStripMenuItem("Show Spectralis");
            var miPlay = new ToolStripMenuItem("Play / Pause");
            var miNext = new ToolStripMenuItem("Next track");
            var miStop = new ToolStripMenuItem("Stop");
            var miSep = new ToolStripSeparator();
            var miExit = new ToolStripMenuItem("Exit");

            miShow.Click += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
            miPlay.Click += (s, e) =>
            {
                if (_engine.IsPlaying) _engine.Pause();
                else _engine.Play();
            };
            miNext.Click += (s, e) =>
            {
                var item = _queue.Next();
                if (item != null) { _engine.Load(item.Track.FilePath); _engine.Play(); }
            };
            miStop.Click += (s, e) => _engine.Stop();
            miExit.Click += (s, e) => Application.Exit();

            menu.Items.Add(miShow);
            menu.Items.Add(miSep);
            menu.Items.Add(miPlay);
            menu.Items.Add(miNext);
            menu.Items.Add(miStop);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(miExit);

            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += (s, e) => ShowRequested?.Invoke(this, EventArgs.Empty);
        }

        public void UpdateTooltip(string title, string artist)
        {
            string text = string.IsNullOrEmpty(title) ? "Spectralis" : $"{artist} — {title}";
            _icon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
