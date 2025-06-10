using System;
using System.Drawing;
using System.Windows.Forms;
using Spectralis.Queue;

namespace Spectralis.UI
{
    public class QueueStatusBar : StatusStrip
    {
        private readonly ToolStripStatusLabel _lblPosition;
        private readonly ToolStripStatusLabel _lblTotal;
        private readonly ToolStripStatusLabel _lblRemaining;

        public QueueStatusBar(PlayQueue queue)
        {
            BackColor = Color.FromArgb(14, 14, 20);
            SizingGrip = false;

            _lblPosition = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(140, 200, 255),
                Font = new Font("Segoe UI", 8f)
            };

            _lblTotal = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(120, 120, 130),
                Font = new Font("Segoe UI", 8f),
                Spring = true,
                TextAlign = ContentAlignment.MiddleCenter
            };

            _lblRemaining = new ToolStripStatusLabel
            {
                ForeColor = Color.FromArgb(100, 100, 110),
                Font = new Font("Segoe UI", 8f),
                Alignment = ToolStripItemAlignment.Right
            };

            Items.Add(_lblPosition);
            Items.Add(_lblTotal);
            Items.Add(_lblRemaining);

            queue.QueueChanged += (s, e) => Update(queue);
            queue.CurrentChanged += (s, e) => Update(queue);

            Update(queue);
        }

        private void Update(PlayQueue queue)
        {
            if (InvokeRequired) { Invoke(new Action(() => Update(queue))); return; }

            int cur = queue.CurrentIndex + 1;
            int total = queue.Count;
            _lblPosition.Text = total > 0 ? $"{cur} / {total}" : "Empty";

            string summary = QueueStats.Summary(queue);
            _lblTotal.Text = summary;

            TimeSpan rem = QueueStats.RemainingDuration(queue);
            _lblRemaining.Text = rem.TotalMinutes >= 1 ? $"{(int)rem.TotalMinutes}m left" : string.Empty;
        }
    }
}
