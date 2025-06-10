using System;
using System.Windows.Forms;

namespace Spectralis.UI
{
    public class PlayerContextMenu : ContextMenuStrip
    {
        public event EventHandler AddToQueueClicked;
        public event EventHandler ShowInExplorerClicked;
        public event EventHandler CopyPathClicked;
        public event EventHandler PropertiesClicked;

        private readonly ToolStripMenuItem _addToQueue;
        private readonly ToolStripMenuItem _showInExplorer;
        private readonly ToolStripMenuItem _copyPath;
        private readonly ToolStripSeparator _sep1;
        private readonly ToolStripMenuItem _properties;

        public PlayerContextMenu()
        {
            _addToQueue = new ToolStripMenuItem("Add to Queue");
            _showInExplorer = new ToolStripMenuItem("Show in Explorer");
            _copyPath = new ToolStripMenuItem("Copy File Path");
            _sep1 = new ToolStripSeparator();
            _properties = new ToolStripMenuItem("Properties");

            _addToQueue.Click += (s, e) => AddToQueueClicked?.Invoke(this, EventArgs.Empty);
            _showInExplorer.Click += (s, e) => ShowInExplorerClicked?.Invoke(this, EventArgs.Empty);
            _copyPath.Click += (s, e) => CopyPathClicked?.Invoke(this, EventArgs.Empty);
            _properties.Click += (s, e) => PropertiesClicked?.Invoke(this, EventArgs.Empty);

            Items.AddRange(new ToolStripItem[]
            {
                _addToQueue,
                _showInExplorer,
                _copyPath,
                _sep1,
                _properties
            });
        }
    }
}
