using System;
using System.Windows.Forms;
using Spectralis.Visualizers;

namespace Spectralis.UI
{
    public class VisualizerContextMenu : ContextMenuStrip
    {
        private readonly VisualizerPanel _panel;
        private readonly VisualizerRegistry _registry;

        public event EventHandler<string> VisualizerSelected;
        public event EventHandler SettingsRequested;
        public event EventHandler ScreenshotRequested;

        public VisualizerContextMenu(VisualizerPanel panel, VisualizerRegistry registry)
        {
            _panel = panel;
            _registry = registry;

            _panel.ContextMenuStrip = this;
            Opening += OnOpening;
        }

        private void OnOpening(object sender, System.ComponentModel.CancelEventArgs e)
        {
            Items.Clear();

            var switchMenu = new ToolStripMenuItem("Switch Visualizer");
            foreach (var name in _registry.GetNames())
            {
                var n = name;
                var item = new ToolStripMenuItem(n);
                item.Click += (s, ev) => VisualizerSelected?.Invoke(this, n);
                switchMenu.DropDownItems.Add(item);
            }
            Items.Add(switchMenu);
            Items.Add(new ToolStripSeparator());

            var settingsItem = new ToolStripMenuItem("Settings...");
            settingsItem.Click += (s, ev) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            Items.Add(settingsItem);

            var ssItem = new ToolStripMenuItem("Save Screenshot");
            ssItem.Click += (s, ev) => ScreenshotRequested?.Invoke(this, EventArgs.Empty);
            Items.Add(ssItem);
        }
    }
}
