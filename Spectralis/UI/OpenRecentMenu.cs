using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using Spectralis.Audio;

namespace Spectralis.UI
{
    public class OpenRecentMenu : ToolStripMenuItem
    {
        private readonly RecentFiles _recentFiles;
        public event EventHandler<string> FileSelected;

        public OpenRecentMenu(RecentFiles recentFiles)
        {
            Text = "Open &Recent";
            _recentFiles = recentFiles;
            DropDownOpening += OnDropDownOpening;
        }

        private void OnDropDownOpening(object sender, EventArgs e)
        {
            DropDownItems.Clear();

            var items = _recentFiles.Items;
            if (items.Count == 0)
            {
                DropDownItems.Add(new ToolStripMenuItem("(empty)") { Enabled = false });
                return;
            }

            foreach (var path in items)
            {
                var displayName = Path.GetFileName(path);
                var item = new ToolStripMenuItem(displayName)
                {
                    ToolTipText = path
                };
                var captured = path;
                item.Click += (s, ev) => FileSelected?.Invoke(this, captured);
                DropDownItems.Add(item);
            }

            DropDownItems.Add(new ToolStripSeparator());
            var clearItem = new ToolStripMenuItem("Clear Recent");
            clearItem.Click += (s, ev) => _recentFiles.Clear();
            DropDownItems.Add(clearItem);
        }
    }
}
