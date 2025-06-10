using System;
using System.Windows.Forms;
using Spectralis.Streaming;

namespace Spectralis.UI
{
    public static class StreamingMenuBuilder
    {
        public static ToolStripMenuItem Build(
            StreamingRegistry registry,
            StreamingAuthStore authStore,
            StreamingPlayerBridge bridge,
            Action showSearchPanel,
            Action showHistoryPanel,
            Action showSettingsPanel)
        {
            var menu = new ToolStripMenuItem("Streaming");

            var miSearch = new ToolStripMenuItem("Search...", null, (s, e) => showSearchPanel());
            miSearch.ShortcutKeys = Keys.Control | Keys.Shift | Keys.F;

            var miHistory = new ToolStripMenuItem("History", null, (s, e) => showHistoryPanel());
            var miSettings = new ToolStripMenuItem("Settings...", null, (s, e) => showSettingsPanel());

            var miSetup = new ToolStripMenuItem("Setup Accounts...", null, (s, e) =>
            {
                using (var dlg = new StreamingAuthDialog(authStore))
                    dlg.ShowDialog();
            });

            var miSeparator = new ToolStripSeparator();

            var miSources = new ToolStripMenuItem("Sources");
            foreach (string name in registry.GetNames())
            {
                string captured = name;
                var src = registry.TryGet(captured);
                var mi = new ToolStripMenuItem(captured)
                {
                    Checked = src?.IsAuthenticated ?? false,
                    CheckOnClick = false
                };
                miSources.DropDownItems.Add(mi);
            }

            menu.DropDownItems.Add(miSearch);
            menu.DropDownItems.Add(miHistory);
            menu.DropDownItems.Add(miSeparator);
            menu.DropDownItems.Add(miSources);
            menu.DropDownItems.Add(new ToolStripSeparator());
            menu.DropDownItems.Add(miSetup);
            menu.DropDownItems.Add(miSettings);

            return menu;
        }
    }
}
