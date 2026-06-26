using System.IO;
using System.Windows.Forms;

namespace Spectralis;

public partial class Form1
{
    private readonly PlayQueue queue = new();
    private bool isQueueVisible;
    private int ctxQueueTargetIndex = -1;
    private bool prevEngineIsPlaying;

    private enum QueueInsertMode
    {
        Next,
        End
    }

    // ── Queue panel toggle ────────────────────────────────────────────────

    private void btnToggleQueue_Click(object sender, EventArgs e) => ToggleQueuePanel();

    private void ToggleQueuePanel()
    {
        isQueueVisible = !isQueueVisible;
        RefreshContentColumns();
        if (isQueueVisible)
        {
            if (IsSpotifyActive)
                _ = RefreshSpotifyQueueAsync(force: true);

            SyncQueueControl();
            lstQueue.ScrollToIndex(IsSpotifyQueueMode ? 0 : queue.CurrentIndex);
        }
        UpdateUiState();
    }

    // ── Content column management ─────────────────────────────────────────

    private void RefreshContentColumns()
    {
        RefreshContentColumns(IsAppLyricsAvailable(GetActiveTrackForUi()));
    }

    private void RefreshContentColumns(bool lyricsOn)
    {
        var showLyrics = lyricsOn && !isQueueVisible;
        var showQueue = isQueueVisible && (!queue.IsEmpty || IsSpotifyQueueMode);

        lyricsView.Visible = showLyrics;
        pnlQueue.Visible = showQueue;

        if (showLyrics)
        {
            contentLayout.ColumnStyles[0] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F);
            contentLayout.ColumnStyles[1] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 42F);
            contentLayout.ColumnStyles[2] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 0F);
        }
        else if (showQueue)
        {
            contentLayout.ColumnStyles[0] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F);
            contentLayout.ColumnStyles[1] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 0F);
            contentLayout.ColumnStyles[2] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 290F);
        }
        else
        {
            contentLayout.ColumnStyles[0] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F);
            contentLayout.ColumnStyles[1] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 0F);
            contentLayout.ColumnStyles[2] = new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Absolute, 0F);
        }

        contentLayout.PerformLayout();
    }

    // ── Queue control sync ────────────────────────────────────────────────

    private void SyncQueueControl()
    {
        if (IsSpotifyQueueMode)
        {
            lstQueue.DisplayItems = GetSpotifyQueueDisplayItems();
            lstQueue.Queue = null;
            lstQueue.Engine = null;
        }
        else
        {
            lstQueue.DisplayItems = null;
            lstQueue.Queue = queue;
            lstQueue.Engine = engine;
        }

        lstQueue.Theme = themePalette;
        lstQueue.Invalidate();
    }

    private bool IsSpotifyQueueMode => IsSpotifyActive;

    private IReadOnlyList<QueueListItem> GetSpotifyQueueDisplayItems()
    {
        if (spotifyQueueItems.Count > 0 || spotifyCurrentTrack is null)
            return spotifyQueueItems;

        return
        [
            new QueueListItem(
                spotifyCurrentTrack.DisplayName,
                spotifyCurrentTrack.Artist,
                IsCurrent: true,
                IsPlaying: spotifyIsPlaying)
        ];
    }

    // ── Event handlers from QueueListControl ─────────────────────────────

    private void lstQueue_ItemActivated(object? sender, int index) => PlayQueueIndex(index);

    private void lstQueue_ItemDeleteRequested(object? sender, int index) => RemoveQueueItem(index);

    private void lstQueue_ItemRightClicked(object? sender, (int Index, Point Location) args)
    {
        ctxQueueTargetIndex = args.Index;
        var idx = args.Index;
        ctxQueuePlay.Enabled      = idx >= 0;
        ctxQueuePlayNext.Enabled  = idx >= 0;
        ctxQueueMoveUp.Enabled    = idx > 0;
        ctxQueueMoveDown.Enabled  = idx >= 0 && idx < queue.Count - 1;
        ctxQueueRemove.Enabled    = idx >= 0;
        ctxQueueEditTw.Enabled    = idx >= 0 &&
                                    !IsSharedQueuePointer(queue.Items[idx]) &&
                                    File.Exists(queue.Items[idx]);
        ctxQueueEditTw.Text       = idx >= 0 && TrackContentWarningStore.HasWarnings(queue.Items[idx])
                                    ? "Content Warnings ✓..."
                                    : "Content Warnings...";
        if (ctxQueueEditTagsItem is not null)
            ctxQueueEditTagsItem.Enabled = idx >= 0 &&
                                           !IsSharedQueuePointer(queue.Items[idx]) &&
                                           File.Exists(queue.Items[idx]);
        ctxQueue.Show(lstQueue, args.Location);
    }

    // ── Context menu handlers ─────────────────────────────────────────────

    private void ctxQueuePlay_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex >= 0) PlayQueueIndex(ctxQueueTargetIndex);
    }

    private void ctxQueuePlayNext_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex < 0) return;
        var insertAt = (queue.CurrentIndex >= 0 ? queue.CurrentIndex : 0) + 1;
        if (insertAt >= queue.Count || ctxQueueTargetIndex == insertAt) return;

        var allPaths = queue.Items.ToList();
        var path = allPaths[ctxQueueTargetIndex];
        allPaths.RemoveAt(ctxQueueTargetIndex);
        var adjusted = ctxQueueTargetIndex < insertAt ? insertAt - 1 : insertAt;
        allPaths.Insert(adjusted, path);

        var currentPath = queue.CurrentPath;
        queue.Clear();
        queue.AddRange(allPaths);
        if (currentPath is not null)
            queue.SetCurrent(allPaths.IndexOf(currentPath));

        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-reorder");
        UpdateUiState();
    }

    private void ctxQueueMoveUp_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex <= 0) return;
        queue.MoveUp(ctxQueueTargetIndex);
        ctxQueueTargetIndex--;
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-reorder");
        UpdateUiState();
    }

    private void ctxQueueMoveDown_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex < 0 || ctxQueueTargetIndex >= queue.Count - 1) return;
        queue.MoveDown(ctxQueueTargetIndex);
        ctxQueueTargetIndex++;
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-reorder");
        UpdateUiState();
    }

    private void ctxQueueRemove_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex >= 0) RemoveQueueItem(ctxQueueTargetIndex);
    }

    private void ctxQueueAddFiles_Click(object? sender, EventArgs e) => AddFilesToQueue();

    private void ctxQueueClear_Click(object? sender, EventArgs e) => ClearQueue();

    private void btnQueueClear_Click(object? sender, EventArgs e) => ClearQueue();

    private void btnQueueShuffle_Click(object? sender, EventArgs e)
    {
        queue.Shuffle = !queue.Shuffle;
        UpdateQueueModeButtons();
    }

    private void btnQueueRepeat_Click(object? sender, EventArgs e)
    {
        queue.Repeat = queue.Repeat switch
        {
            RepeatMode.None => RepeatMode.All,
            RepeatMode.All  => RepeatMode.One,
            _               => RepeatMode.None
        };
        UpdateQueueModeButtons();
    }

    // ── Queue operations ──────────────────────────────────────────────────

    private void PlayQueueIndex(int index)
    {
        var path = queue.SetCurrent(index);
        if (path is null) return;
        _ = PlayQueueItemAsync(path, startPlayback: true);
    }

    private void RemoveQueueItem(int index)
    {
        var wasCurrentTrack = index == queue.CurrentIndex;
        queue.Remove(index);
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-remove");

        if (queue.IsEmpty)
        {
            if (wasCurrentTrack)
            {
                CancelSpotifyLocalHandoff();
                engine.Stop();
            }
            if (isQueueVisible) RefreshContentColumns();
        }

        UpdateUiState();
    }

    private void ClearQueue()
    {
        CancelSpotifyLocalHandoff();
        queue.Clear();
        engine.Stop();
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-clear");
        RefreshContentColumns();
        UpdateUiState();
    }

    private void AddFilesToQueue()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = SupportedAudioFormats.OpenFileDialogFilter,
            Title = "Add files to queue",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        QueueLocalFiles(dialog.FileNames.Where(File.Exists).ToArray(), playIfQueueWasEmpty: appSettings.AutoPlayOnOpen);
    }

    private void QueueLocalFiles(
        string[] paths,
        bool playIfQueueWasEmpty,
        QueueInsertMode insertMode = QueueInsertMode.Next)
    {
        if (paths.Length == 0) return;

        QueueItems(paths, playIfQueueWasEmpty, insertMode);
    }

    private void QueueExternalPointers(
        string[] pointers,
        bool playIfQueueWasEmpty,
        QueueInsertMode insertMode = QueueInsertMode.Next)
    {
        var validPointers = pointers
            .Select(static pointer => pointer.Trim())
            .Where(static pointer => !string.IsNullOrWhiteSpace(pointer))
            .ToArray();
        if (validPointers.Length == 0) return;

        QueueItems(validPointers, playIfQueueWasEmpty, insertMode);
    }

    private void QueueItems(string[] items, bool playIfQueueWasEmpty, QueueInsertMode insertMode)
    {
        var wasEmpty = queue.IsEmpty;
        var insertAt = insertMode == QueueInsertMode.End
            ? queue.Count
            : queue.CurrentIndex >= 0 ? queue.CurrentIndex + 1 : queue.Count;
        queue.InsertRange(insertAt, items);
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-add");

        if (wasEmpty || queue.CurrentIndex < 0)
        {
            var path = queue.SetCurrent(0);
            if (path is not null)
                _ = PlayQueueItemAsync(path, playIfQueueWasEmpty);
        }

        if (isQueueVisible) RefreshContentColumns();
        UpdateUiState();
    }

    private void StartLocalInterludeFromSpotify(string[] paths)
    {
        if (paths.Length == 0) return;

        ParkSpotifyForLocalPlayback(resumeAfterLocalPlayback: true, advanceOnResume: true);
        if (resumeSpotifyAfterLocalPlayback)
            BeginSpotifyLocalInterlude();

        queue.Clear();
        queue.AddRange(paths);
        var path = queue.SetCurrent(0);
        SyncQueueControl();
        NotifySharedPlayPlaybackChanged("queue-add");

        if (path is not null)
            _ = PlayQueueItemAsync(path, startPlayback: true);

        if (isQueueVisible) RefreshContentColumns();
        UpdateUiState();
    }

    private void NavigateNext()
    {
        if (IsSpotifyActive)
        {
            _ = SpotifyNextAsync();
            return;
        }

        var path = queue.MoveNext();
        if (path is null)
        {
            if (resumeSpotifyAfterLocalPlayback)
                _ = ResumeSpotifyAfterLocalPlaybackAsync();
            return;
        }
        _ = PlayQueueItemAsync(path, startPlayback: true);
    }

    private void NavigatePrevious()
    {
        if (IsSpotifyActive)
        {
            if (spotifyPositionSeconds > 3f)
                _ = SpotifySeekAsync(0);
            else
                _ = SpotifyPreviousAsync();
            return;
        }

        if (engine.IsLoaded && engine.GetPosition() > 3f && queue.Repeat != RepeatMode.One)
        {
            engine.Seek(0);
            NotifySharedPlayPlaybackChanged("seek");
            UpdateUiState();
            return;
        }

        var path = queue.MovePrevious();
        if (path is null) return;
        _ = PlayQueueItemAsync(path, startPlayback: true);
    }

    private void CheckAutoAdvance()
    {
        var isPlaying = engine.IsPlaying;
        var length = engine.GetLength();
        var position = engine.GetPosition();
        var reachedEnd = length > 0 && position >= Math.Max(0, length - 0.25f);

        if (prevEngineIsPlaying && !isPlaying && engine.IsLoaded && reachedEnd && !IsAlbumWorldActive)
        {
            if (queue.HasNext)
                NavigateNext();
            else if (resumeSpotifyAfterLocalPlayback)
                _ = ResumeSpotifyAfterLocalPlaybackAsync();
        }
        prevEngineIsPlaying = isPlaying;
    }

    // ── Queue mode button appearance ──────────────────────────────────────

    private void UpdateQueueModeButtons()
    {
        if (IsSpotifyQueueMode)
        {
            btnQueueShuffle.Enabled = false;
            btnQueueRepeat.Enabled = false;
            btnQueueClear.Enabled = false;
            btnQueueShuffle.AccentColor = AccentSoftColor;
            btnQueueShuffle.ForeColor = TextMutedColor;
            btnQueueRepeat.AccentColor = AccentSoftColor;
            btnQueueRepeat.ForeColor = TextMutedColor;
            btnQueueClear.AccentColor = AccentSoftColor;
            btnQueueClear.ForeColor = TextMutedColor;
            lstQueue.Invalidate();
            return;
        }

        btnQueueShuffle.Enabled = true;
        btnQueueRepeat.Enabled = true;
        btnQueueClear.Enabled = true;
        btnQueueRepeat.Text = queue.Repeat switch
        {
            RepeatMode.All => "Repeat: All",
            RepeatMode.One => "Repeat: One",
            _              => "Repeat: Off"
        };
        btnQueueRepeat.AccentColor = queue.Repeat != RepeatMode.None ? AccentPrimaryColor : AccentSoftColor;
        btnQueueRepeat.ForeColor   = queue.Repeat != RepeatMode.None ? AccentContrastColor : TextMutedColor;

        btnQueueShuffle.AccentColor = queue.Shuffle ? AccentPrimaryColor : AccentSoftColor;
        btnQueueShuffle.ForeColor   = queue.Shuffle ? AccentContrastColor : TextMutedColor;

        lstQueue.Invalidate();
    }

    private void UpdateQueuePanel()
    {
        if (!isQueueVisible) return;
        SyncQueueControl();

        if (IsSpotifyQueueMode)
        {
            var spotifyCount = GetSpotifyQueueDisplayItems().Count;
            lblQueueHeader.Text = spotifyCount switch
            {
                0 => "Spotify Queue",
                1 => "Spotify Queue  ·  Current track",
                _ => $"Spotify Queue  ·  {spotifyCount - 1} upcoming"
            };
            UpdateQueueModeButtons();
            lstQueue.ScrollToIndex(0);
            return;
        }

        var count = queue.Count;
        lblQueueHeader.Text = count switch
        {
            0 => "Queue",
            1 => "Queue  ·  1 track",
            _ => $"Queue  ·  {count} tracks"
        };
        lstQueue.ScrollToIndex(queue.CurrentIndex);
    }

    private void ctxQueueEditTw_Click(object? sender, EventArgs e)
    {
        if (ctxQueueTargetIndex < 0) return;
        var path = queue.Items[ctxQueueTargetIndex];
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        OpenContentWarningEditor(path);
    }

    private void OpenContentWarningEditor(string path)
    {
        using var dlg = new ContentWarningEditDialog(path, themePalette);
        dlg.ShowDialog(this);
        // Refresh the queue display so the ✓ indicator updates on next right-click
        SyncQueueControl();
    }

    private void fileAddToQueueToolStripMenuItem_Click(object sender, EventArgs e) => AddFilesToQueue();
    private void playbackNextToolStripMenuItem_Click(object sender, EventArgs e) => NavigateNext();
    private void playbackPreviousToolStripMenuItem_Click(object sender, EventArgs e) => NavigatePrevious();
    private void btnPrevious_Click(object sender, EventArgs e) => NavigatePrevious();
    private void btnNext_Click(object sender, EventArgs e) => NavigateNext();
}
