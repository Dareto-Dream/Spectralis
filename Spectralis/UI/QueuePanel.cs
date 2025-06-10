using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class QueuePanel : Panel
    {
        private readonly PlayQueue _queue;
        private readonly ListView _list;
        private readonly ToolStrip _toolbar;
        private readonly ToolStripButton _btnShuffle;
        private readonly ToolStripButton _btnRepeat;
        private readonly ToolStripButton _btnClear;

        public event EventHandler<PlayQueueItem> ItemActivated;

        public QueuePanel(PlayQueue queue)
        {
            _queue = queue;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(16, 16, 22);

            _toolbar = new ToolStrip
            {
                GripStyle = ToolStripGripStyle.Hidden,
                BackColor = Color.FromArgb(22, 22, 30),
                RenderMode = ToolStripRenderMode.System
            };

            _btnShuffle = new ToolStripButton("Shuffle") { CheckOnClick = true, ForeColor = Color.FromArgb(180, 180, 180) };
            _btnRepeat = new ToolStripButton("Repeat: Off") { ForeColor = Color.FromArgb(180, 180, 180) };
            _btnClear = new ToolStripButton("Clear") { ForeColor = Color.FromArgb(180, 180, 180) };

            _toolbar.Items.Add(_btnShuffle);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnRepeat);
            _toolbar.Items.Add(new ToolStripSeparator());
            _toolbar.Items.Add(_btnClear);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                AllowDrop = true,
                BackColor = Color.FromArgb(16, 16, 22),
                ForeColor = Color.FromArgb(210, 210, 210),
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 9f)
            };
            _list.Columns.Add("#", 30);
            _list.Columns.Add("Title", 200);
            _list.Columns.Add("Artist", 150);
            _list.Columns.Add("Duration", 60);

            Controls.Add(_list);
            Controls.Add(_toolbar);

            _queue.QueueChanged += (s, e) => Reload();
            _queue.CurrentChanged += (s, e) => HighlightCurrent();

            _btnShuffle.CheckedChanged += (s, e) => _queue.SetShuffle(_btnShuffle.Checked);
            _btnRepeat.Click += OnRepeatClick;
            _btnClear.Click += (s, e) => _queue.Clear();

            _list.DoubleClick += (s, e) =>
            {
                if (_list.SelectedItems.Count == 0) return;
                int idx = _list.SelectedItems[0].Index;
                var item = _queue.PlayAt(idx);
                if (item != null) ItemActivated?.Invoke(this, item);
            };

            _list.ItemDrag += OnItemDrag;
            _list.DragOver += (s, e) => e.Effect = DragDropEffects.Move;
            _list.DragDrop += OnDragDrop;

            Reload();
        }

        private void OnRepeatClick(object sender, EventArgs e)
        {
            _queue.RepeatMode = _queue.RepeatMode switch
            {
                RepeatMode.None => RepeatMode.RepeatAll,
                RepeatMode.RepeatAll => RepeatMode.RepeatOne,
                _ => RepeatMode.None
            };

            _btnRepeat.Text = _queue.RepeatMode switch
            {
                RepeatMode.RepeatAll => "Repeat: All",
                RepeatMode.RepeatOne => "Repeat: One",
                _ => "Repeat: Off"
            };
        }

        private void OnItemDrag(object sender, ItemDragEventArgs e)
        {
            DoDragDrop(e.Item, DragDropEffects.Move);
        }

        private void OnDragDrop(object sender, DragEventArgs e)
        {
            var dragged = (ListViewItem)e.Data.GetData(typeof(ListViewItem));
            if (dragged == null) return;

            Point pt = _list.PointToClient(new Point(e.X, e.Y));
            var target = _list.GetItemAt(pt.X, pt.Y);
            if (target == null || target.Index == dragged.Index) return;

            _queue.Move(dragged.Index, target.Index);
        }

        private void Reload()
        {
            if (InvokeRequired) { Invoke(new Action(Reload)); return; }
            _list.Items.Clear();
            int i = 0;
            foreach (var item in _queue.Items)
            {
                i++;
                var lvi = new ListViewItem(i.ToString());
                lvi.SubItems.Add(item.Track?.Title ?? "?");
                lvi.SubItems.Add(item.Track?.Artist ?? "?");
                lvi.SubItems.Add(item.Track?.Duration.ToString(@"m\:ss") ?? "?");
                _list.Items.Add(lvi);
            }
            HighlightCurrent();
        }

        private void HighlightCurrent()
        {
            if (InvokeRequired) { Invoke(new Action(HighlightCurrent)); return; }
            for (int i = 0; i < _list.Items.Count; i++)
            {
                bool isCurrent = i == _queue.CurrentIndex;
                _list.Items[i].Font = isCurrent
                    ? new Font("Segoe UI", 9f, FontStyle.Bold)
                    : new Font("Segoe UI", 9f);
                _list.Items[i].ForeColor = isCurrent
                    ? Color.FromArgb(100, 200, 255)
                    : Color.FromArgb(210, 210, 210);
            }
        }
    }
}
