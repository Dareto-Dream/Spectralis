using System.IO;

namespace Spectralis;

public partial class Form1
{
    private readonly MusicLibrary  musicLibrary  = new();
    private readonly LibraryWatcher libraryWatcher = new();
    private LibraryStore?          libraryStore;
    private LibraryBrowserControl? libraryBrowser;
    private bool                   isLibraryVisible;
    private ToolStripMenuItem?     mniLibrary;

    private void InitializeLibrary()
    {
        libraryStore = new LibraryStore();
        musicLibrary.Initialize(libraryStore);

        libraryBrowser = new LibraryBrowserControl
        {
            Visible = false,
            Dock    = DockStyle.Fill
        };
        libraryBrowser.SetLibrary(musicLibrary);
        libraryBrowser.SetGetFoldersCallback(() => [.. appSettings.LibraryFolders]);
        libraryBrowser.TrackActivated    += LibraryBrowser_TrackActivated;
        libraryBrowser.AddFolderRequested += (_, _) => ShowLibrarySettings();
        libraryBrowser.EditTagsRequested  += (_, path) => OpenTagEditor([path]);
        libraryBrowser.ApplyTheme(themePalette);

        contentLayout.Controls.Add(libraryBrowser, 0, 0);
        contentLayout.SetColumnSpan(libraryBrowser, contentLayout.ColumnCount);
        libraryBrowser.BringToFront();

        // ── File menu entries ────────────────────────────────────────────────
        mniLibrary = new ToolStripMenuItem
        {
            Text         = "Library",
            Name         = "mniLibrary",
            ShortcutKeys = Keys.Control | Keys.B
        };
        mniLibrary.Click += (_, _) => ShowLibraryView();

        var mniLibSettings = new ToolStripMenuItem
        {
            Text = "Library Settings...",
            Name = "mniLibrarySettings"
        };
        mniLibSettings.Click += (_, _) => ShowLibrarySettings();

        libraryToolStripMenuItem.DropDownItems.Add(mniLibrary);
        libraryToolStripMenuItem.DropDownItems.Add(mniLibSettings);
        libraryToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());

        // ── File system watcher ──────────────────────────────────────────────
        libraryWatcher.FileAdded   += (_, path) => IndexSingleFile(path);
        libraryWatcher.FileRemoved += (_, path) => musicLibrary.Remove(path);
        libraryWatcher.FileRenamed += (_, e)    =>
        {
            musicLibrary.Remove(e.OldPath);
            IndexSingleFile(e.NewPath);
        };
        libraryWatcher.Watch(appSettings.LibraryFolders);

        // ── Auto-scan ────────────────────────────────────────────────────────
        if (appSettings.LibraryAutoScanOnOpen && appSettings.LibraryFolders.Count > 0)
            _ = libraryBrowser.StartScanAsync();
    }

    // ── Library panel toggle ─────────────────────────────────────────────────

    private void ShowLibraryView()
    {
        SetContentWorkspace(ContentWorkspace.Library);
    }

    // ── Library settings ─────────────────────────────────────────────────────

    private void ShowLibrarySettings()
    {
        using var dlg = new LibrarySettingsDialog(appSettings, StartLibraryRescan, themePalette);
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        SaveAppSettings();
        libraryWatcher.Watch(appSettings.LibraryFolders);
        libraryBrowser?.SetGetFoldersCallback(() => [.. appSettings.LibraryFolders]);
    }

    private void StartLibraryRescan()
    {
        if (libraryBrowser is not null)
            _ = libraryBrowser.StartScanAsync();
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void LibraryBrowser_TrackActivated(object? sender, string path)
    {
        if (!File.Exists(path)) return;
        QueueLocalFiles([path], playIfQueueWasEmpty: true);
    }

    // Called by Form1.Playback.cs after a local file loads successfully
    partial void OnLocalFileLoaded(string path)
    {
        if (!string.IsNullOrWhiteSpace(path))
            musicLibrary.IncrementPlayCount(path);
    }

    // ── Incremental indexing ──────────────────────────────────────────────────

    private void IndexSingleFile(string path)
    {
        _ = Task.Run(() =>
        {
            try
            {
                var existing = musicLibrary.Find(path);
                using var file = TagLib.File.Create(path);
                var tag = file.Tag;
                var track = new LibraryTrack(
                    Path:            path,
                    Title:           string.IsNullOrWhiteSpace(tag.Title)
                                     ? System.IO.Path.GetFileNameWithoutExtension(path)
                                     : tag.Title.Trim(),
                    Artist:          tag.FirstPerformer?.Trim() ?? "",
                    Album:           tag.Album?.Trim() ?? "",
                    AlbumArtist:     tag.FirstAlbumArtist?.Trim() ?? "",
                    Genre:           tag.FirstGenre?.Trim() ?? "",
                    Year:            (int)tag.Year,
                    DurationSeconds: file.Properties.Duration.TotalSeconds,
                    PlayCount:       existing?.PlayCount ?? 0,
                    DateAdded:       existing?.DateAdded ?? DateTime.UtcNow,
                    LastPlayed:      existing?.LastPlayed
                );
                musicLibrary.Upsert(track);
            }
            catch { }
        });
    }

    // ── Theme ─────────────────────────────────────────────────────────────────

    private void ApplyLibraryTheme()
    {
        libraryBrowser?.ApplyTheme(themePalette);
    }
}
