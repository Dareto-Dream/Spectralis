using System;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.UI
{
    public class LibraryContextMenu : ContextMenuStrip
    {
        public event EventHandler<LibraryTrack> PlayNow;
        public event EventHandler<LibraryTrack> AddToQueue;
        public event EventHandler<LibraryTrack> PlayNext;
        public event EventHandler<LibraryTrack> ShowInExplorer;

        private LibraryTrack _track;

        public LibraryContextMenu()
        {
            var playNow = new ToolStripMenuItem("Play Now");
            var playNext = new ToolStripMenuItem("Play Next");
            var addToQueue = new ToolStripMenuItem("Add to Queue");
            var sep = new ToolStripSeparator();
            var showInExplorer = new ToolStripMenuItem("Show in Explorer");

            playNow.Click += (s, e) => PlayNow?.Invoke(this, _track);
            playNext.Click += (s, e) => PlayNext?.Invoke(this, _track);
            addToQueue.Click += (s, e) => AddToQueue?.Invoke(this, _track);
            showInExplorer.Click += (s, e) => ShowInExplorer?.Invoke(this, _track);

            Items.AddRange(new ToolStripItem[] { playNow, playNext, addToQueue, sep, showInExplorer });
        }

        public void Show(LibraryTrack track, System.Drawing.Point location)
        {
            _track = track;
            Show(location);
        }
    }
}
