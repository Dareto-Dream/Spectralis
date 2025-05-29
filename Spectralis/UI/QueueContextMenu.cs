using System;
using System.Windows.Forms;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class QueueContextMenu : ContextMenuStrip
    {
        private readonly PlayQueue _queue;

        public event EventHandler<int> PlayRequested;
        public event EventHandler<int> RemoveRequested;
        public event EventHandler MoveUpRequested;
        public event EventHandler MoveDownRequested;

        private int _targetIndex = -1;

        public QueueContextMenu(PlayQueue queue)
        {
            _queue = queue;

            var miPlay = new ToolStripMenuItem("Play now");
            var miRemove = new ToolStripMenuItem("Remove from queue");
            var miMoveUp = new ToolStripMenuItem("Move up");
            var miMoveDown = new ToolStripMenuItem("Move down");
            var miSep = new ToolStripSeparator();
            var miClear = new ToolStripMenuItem("Clear queue");

            miPlay.Click += (s, e) => PlayRequested?.Invoke(this, _targetIndex);
            miRemove.Click += (s, e) => RemoveRequested?.Invoke(this, _targetIndex);
            miMoveUp.Click += (s, e) =>
            {
                if (_targetIndex > 0) { _queue.Move(_targetIndex, _targetIndex - 1); MoveUpRequested?.Invoke(this, EventArgs.Empty); }
            };
            miMoveDown.Click += (s, e) =>
            {
                if (_targetIndex < _queue.Count - 1) { _queue.Move(_targetIndex, _targetIndex + 1); MoveDownRequested?.Invoke(this, EventArgs.Empty); }
            };
            miClear.Click += (s, e) => _queue.Clear();

            Items.Add(miPlay);
            Items.Add(miSep);
            Items.Add(miMoveUp);
            Items.Add(miMoveDown);
            Items.Add(miRemove);
            Items.Add(new ToolStripSeparator());
            Items.Add(miClear);
        }

        public void ShowFor(int index, int x, int y)
        {
            _targetIndex = index;
            Show(x, y);
        }
    }
}
