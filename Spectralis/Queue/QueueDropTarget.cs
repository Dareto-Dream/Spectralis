using System;
using System.Windows.Forms;
using Spectralis.Library;

namespace Spectralis.Queue
{
    public class QueueDropTarget
    {
        private readonly PlayQueue _queue;

        public QueueDropTarget(PlayQueue queue)
        {
            _queue = queue;
        }

        public void Register(Control target)
        {
            target.AllowDrop = true;
            target.DragEnter += OnDragEnter;
            target.DragDrop += OnDragDrop;
        }

        public void Unregister(Control target)
        {
            target.DragEnter -= OnDragEnter;
            target.DragDrop -= OnDragDrop;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfo)) ||
                e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(TrackInfo)))
            {
                var track = (TrackInfo)e.Data.GetData(typeof(TrackInfo));
                _queue.Add(new PlayQueueItem(track));
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (string file in files)
                {
                    _queue.Add(new PlayQueueItem(new TrackInfo { FilePath = file, Title = System.IO.Path.GetFileNameWithoutExtension(file) }));
                }
            }
        }
    }
}
