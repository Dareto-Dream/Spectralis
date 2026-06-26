namespace Spectralis;

public partial class Form1
{
    private void NormalizeWorkflowMenus()
    {
        NormalizeLibraryMenu();
        NormalizeToolsMenu();
    }

    private void NormalizeLibraryMenu()
    {
        var showLibrary = TakeMenuItem(libraryToolStripMenuItem, "mniLibrary");
        var showPlaylists = TakeMenuItem(libraryToolStripMenuItem, "mniPlaylists");
        var openM3u = TakeMenuItem(libraryToolStripMenuItem, "mniOpenM3u");
        var saveQueue = TakeMenuItem(libraryToolStripMenuItem, "mniSaveQueueAsPlaylist");
        var analyze = TakeMenuItem(libraryToolStripMenuItem, "mniAnalyzeAllBpm");
        var stats = TakeMenuItem(libraryToolStripMenuItem, "mniListeningStats");
        var scrobbling = TakeMenuItem(libraryToolStripMenuItem, "mniScrobbling");
        var settings = TakeMenuItem(libraryToolStripMenuItem, "mniLibrarySettings");

        libraryToolStripMenuItem.DropDownItems.Clear();

        AddMenuItem(libraryToolStripMenuItem, CreateNowPlayingMenuItem(), "&Now Playing");
        AddMenuItem(libraryToolStripMenuItem, showLibrary, "&Library Browser");
        AddMenuItem(libraryToolStripMenuItem, showPlaylists, "&Playlists");
        AddSeparatorIfNeeded(libraryToolStripMenuItem);
        AddMenuItem(libraryToolStripMenuItem, openM3u, "Open Playlist (&M3U)...");
        AddMenuItem(libraryToolStripMenuItem, saveQueue, "Save Queue as Playlist...");
        AddSeparatorIfNeeded(libraryToolStripMenuItem);
        AddMenuItem(libraryToolStripMenuItem, analyze, "Analyze BPM + &Key");
        AddMenuItem(libraryToolStripMenuItem, stats, "Listening &Stats...");
        AddSeparatorIfNeeded(libraryToolStripMenuItem);
        AddMenuItem(libraryToolStripMenuItem, scrobbling, "&Scrobbling Settings...");
        AddMenuItem(libraryToolStripMenuItem, settings, "Library &Folders...");

        TrimTrailingSeparators(libraryToolStripMenuItem);
        UpdateWorkspaceMenuState();
    }

    private void NormalizeToolsMenu()
    {
        var effects = TakeMenuItem(toolsToolStripMenuItem, "mniEffectsChain");
        var karaoke = TakeMenuItem(toolsToolStripMenuItem, "mniKaraokeMode");
        var metronome = TakeMenuItem(toolsToolStripMenuItem, "mniMetronome");
        var scriptedVisualizers = TakeMenuItem(toolsToolStripMenuItem, "mniScriptedVisualizers");
        var lyricsStudio = TakeMenuItem(toolsToolStripMenuItem, "fileLyricsTimingStudioToolStripMenuItem");
        var obs = TakeMenuItem(toolsToolStripMenuItem, "obsToolStripMenuItem");
        var songWars = TakeMenuItem(toolsToolStripMenuItem, "songWarsMenuItem");

        toolsToolStripMenuItem.DropDownItems.Clear();

        var audioMenu = CreateSubMenu("toolsAudioMenuItem", "&Audio");
        AddMenuItem(audioMenu, effects, "Effects Chain...");
        AddMenuItem(audioMenu, karaoke, "Karaoke Mode...");
        AddMenuItem(audioMenu, metronome, "Metronome...");
        AddSubMenuIfNeeded(toolsToolStripMenuItem, audioMenu);

        var creatorMenu = CreateSubMenu("toolsCreatorMenuItem", "&Creator");
        AddMenuItem(creatorMenu, scriptedVisualizers, "Scripted Visualizers...");
        AddMenuItem(creatorMenu, lyricsStudio, "Lyrics Timing Studio...");
        AddSubMenuIfNeeded(toolsToolStripMenuItem, creatorMenu);

        var liveMenu = CreateSubMenu("toolsLiveMenuItem", "&Live");
        AddMenuItem(liveMenu, obs, "OBS Overlay...");
        AddMenuItem(liveMenu, songWars, "Song Wars...");
        AddSubMenuIfNeeded(toolsToolStripMenuItem, liveMenu);
    }

    private static ToolStripMenuItem? TakeMenuItem(ToolStripMenuItem parent, string name)
    {
        for (var i = 0; i < parent.DropDownItems.Count; i++)
        {
            var item = parent.DropDownItems[i];
            if (item is ToolStripMenuItem menuItem &&
                string.Equals(menuItem.Name, name, StringComparison.Ordinal))
            {
                parent.DropDownItems.Remove(menuItem);
                return menuItem;
            }
        }

        return null;
    }

    private static ToolStripMenuItem CreateSubMenu(string name, string text) =>
        new()
        {
            Name = name,
            Text = text
        };

    private static void AddMenuItem(ToolStripMenuItem parent, ToolStripMenuItem? item, string text)
    {
        if (item is null)
            return;

        item.Text = text;
        parent.DropDownItems.Add(item);
    }

    private static void AddSubMenuIfNeeded(ToolStripMenuItem parent, ToolStripMenuItem submenu)
    {
        if (submenu.DropDownItems.Count == 0)
            return;

        parent.DropDownItems.Add(submenu);
    }

    private static void AddSeparatorIfNeeded(ToolStripMenuItem parent)
    {
        if (parent.DropDownItems.Count == 0 ||
            parent.DropDownItems[parent.DropDownItems.Count - 1] is ToolStripSeparator)
        {
            return;
        }

        parent.DropDownItems.Add(new ToolStripSeparator());
    }

    private static void TrimTrailingSeparators(ToolStripMenuItem parent)
    {
        while (parent.DropDownItems.Count > 0 &&
               parent.DropDownItems[parent.DropDownItems.Count - 1] is ToolStripSeparator separator)
        {
            parent.DropDownItems.Remove(separator);
            separator.Dispose();
        }
    }
}
