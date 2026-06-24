using System.IO;

namespace Spectralis;

public partial class Form1
{
    private PlaylistBrowserControl? playlistBrowser;
    private bool isPlaylistsVisible;
    private ToolStripMenuItem? mniPlaylists;
    private ToolStripMenuItem? ctxQueueSavePlaylistItem;

    private void InitializePlaylists()
    {
        PlaylistStore.EnsureDirectory();

        playlistBrowser = new PlaylistBrowserControl
        {
            Visible = false,
            Dock    = DockStyle.Fill,
        };
        playlistBrowser.SetLibrary(musicLibrary);
        playlistBrowser.LoadPlaylists(PlaylistStore.LoadAll(), PlaylistStore.LoadAllSmart());
        playlistBrowser.PlayRequested        += PlaylistBrowser_PlayRequested;
        playlistBrowser.EditRequested        += PlaylistBrowser_EditRequested;
        playlistBrowser.EditSmartRequested   += PlaylistBrowser_EditSmartRequested;
        playlistBrowser.DeleteRequested      += PlaylistBrowser_DeleteRequested;
        playlistBrowser.DeleteSmartRequested += PlaylistBrowser_DeleteSmartRequested;
        playlistBrowser.NewPlaylistRequested     += (_, _) => CreateNewPlaylist();
        playlistBrowser.NewSmartPlaylistRequested += (_, _) => CreateNewSmartPlaylist();
        playlistBrowser.ImportRequested      += (_, _) => ImportM3u();
        playlistBrowser.ApplyTheme(themePalette);

        contentLayout.Controls.Add(playlistBrowser, 0, 0);
        contentLayout.SetColumnSpan(playlistBrowser, contentLayout.ColumnCount);
        playlistBrowser.BringToFront();

        // ── File menu items ──────────────────────────────────────────────────
        mniPlaylists = new ToolStripMenuItem
        {
            Name         = "mniPlaylists",
            Text         = "Playlists",
            ShortcutKeys = Keys.Control | Keys.P,
        };
        mniPlaylists.Click += (_, _) => ShowPlaylistsView();

        var mniSaveQueue = new ToolStripMenuItem
        {
            Name = "mniSaveQueueAsPlaylist",
            Text = "Save Queue as Playlist...",
        };
        mniSaveQueue.Click += (_, _) => SaveQueueAsPlaylist();

        var mniOpenM3u = new ToolStripMenuItem
        {
            Name = "mniOpenM3u",
            Text = "Open Playlist (M3U)...",
        };
        mniOpenM3u.Click += (_, _) => OpenM3uAndLoad();

        libraryToolStripMenuItem.DropDownItems.Add(mniPlaylists);
        libraryToolStripMenuItem.DropDownItems.Add(mniSaveQueue);
        libraryToolStripMenuItem.DropDownItems.Add(mniOpenM3u);
        libraryToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

        // ── Queue context menu ───────────────────────────────────────────────
        ctxQueueSavePlaylistItem = new ToolStripMenuItem
        {
            Name = "ctxQueueSavePlaylist",
            Text = "Save Queue as Playlist...",
        };
        ctxQueueSavePlaylistItem.Click += (_, _) => SaveQueueAsPlaylist();
        ctxQueue.Items.Insert(ctxQueue.Items.IndexOf(ctxQueueAddFiles), ctxQueueSavePlaylistItem);
    }

    private void ShowPlaylistsView()
    {
        SetContentWorkspace(ContentWorkspace.Playlists);
    }

    // ── Playlist CRUD ─────────────────────────────────────────────────────────

    private void CreateNewPlaylist()
    {
        using var nameDlg = new NameInputDialog("New Playlist", "Playlist name:", "New Playlist", themePalette);
        if (nameDlg.ShowDialog(this) != DialogResult.OK) return;

        var pl = new Playlist { Name = nameDlg.InputValue };
        using var editDlg = new PlaylistEditorDialog(pl, themePalette, musicLibrary, queue);
        if (editDlg.ShowDialog(this) != DialogResult.OK) return;

        PlaylistStore.Save(pl);
        playlistBrowser?.AddPlaylist(pl);
    }

    private void CreateNewSmartPlaylist()
    {
        var pl = new SmartPlaylist { Name = "New Smart Playlist" };
        using var dlg = new SmartPlaylistEditorDialog(pl, themePalette);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        PlaylistStore.SaveSmart(pl);
        playlistBrowser?.AddSmartPlaylist(pl);
    }

    private void PlaylistBrowser_PlayRequested(object? sender, string[] paths)
    {
        var existing = paths.Where(File.Exists).ToArray();
        if (existing.Length == 0) return;

        LoadFilesAsQueue(existing, startPlayback: appSettings.AutoPlayOnOpen);
    }

    private void PlaylistBrowser_EditRequested(object? sender, Guid id)
    {
        var pl = PlaylistStore.LoadAll().Find(p => p.Id == id);
        if (pl is null) return;

        using var dlg = new PlaylistEditorDialog(pl, themePalette, musicLibrary, queue);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        PlaylistStore.Save(pl);
        playlistBrowser?.UpdatePlaylist(pl);
    }

    private void PlaylistBrowser_EditSmartRequested(object? sender, Guid id)
    {
        var pl = PlaylistStore.LoadAllSmart().Find(p => p.Id == id);
        if (pl is null) return;

        using var dlg = new SmartPlaylistEditorDialog(pl, themePalette);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        PlaylistStore.SaveSmart(pl);
        playlistBrowser?.UpdateSmartPlaylist(pl);
    }

    private void PlaylistBrowser_DeleteRequested(object? sender, Guid id)
    {
        PlaylistStore.Delete(id);
        playlistBrowser?.RemovePlaylist(id);
    }

    private void PlaylistBrowser_DeleteSmartRequested(object? sender, Guid id)
    {
        PlaylistStore.DeleteSmart(id);
        playlistBrowser?.RemoveSmartPlaylist(id);
    }

    // ── Save queue as playlist ────────────────────────────────────────────────

    private void SaveQueueAsPlaylist()
    {
        if (queue.IsEmpty)
        {
            MessageBox.Show(this, "The queue is empty.", "Save as Playlist", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var nameDlg = new NameInputDialog("Save as Playlist", "Playlist name:", "My Playlist", themePalette);
        if (nameDlg.ShowDialog(this) != DialogResult.OK) return;

        var pl = queue.SaveAsPlaylist(nameDlg.InputValue, musicLibrary);
        PlaylistStore.Save(pl);
        playlistBrowser?.AddPlaylist(pl);

        ShowPlaylistsView();
    }

    // ── M3U import / open ─────────────────────────────────────────────────────

    private void ImportM3u()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Playlist files|*.m3u;*.m3u8|All files|*.*",
            Title  = "Import Playlist",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var items = M3uParser.ImportItems(dlg.FileName);
            var name  = System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);
            var pl    = new Playlist
            {
                Name  = name,
                Items = items,
            };
            PlaylistStore.Save(pl);
            playlistBrowser?.AddPlaylist(pl);
        }
        catch (Exception ex)
        {
            ShowError($"Could not import playlist:{Environment.NewLine}{ex.Message}", "Import Error");
        }
    }

    private void OpenM3uAndLoad()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Playlist files|*.m3u;*.m3u8|All files|*.*",
            Title  = "Open Playlist",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var paths = M3uParser.Import(dlg.FileName).Where(File.Exists).ToArray();
            if (paths.Length == 0) return;
            LoadFilesAsQueue(paths, startPlayback: appSettings.AutoPlayOnOpen);
        }
        catch (Exception ex)
        {
            ShowError($"Could not open playlist:{Environment.NewLine}{ex.Message}", "Open Error");
        }
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void ApplyPlaylistsTheme()
    {
        playlistBrowser?.ApplyTheme(themePalette);
    }
}
