using System.IO;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private AlbumWorldRuntime? albumWorldRuntime;
    private AlbumWorldFallbackControl? albumWorldFallbackControl;
    private AlbumWorldCacheStore? albumWorldCacheStore;
    private string? activeAlbumDir;
    private bool albumWorldShowingWorld;

    private bool IsAlbumWorldActive => albumWorldRuntime is { IsActive: true };

    private void InitializeAlbumWorld()
    {
        albumWorldCacheStore = new AlbumWorldCacheStore();
        albumWorldRuntime = new AlbumWorldRuntime();

        albumWorldRuntime.PlayTrackRequested += (_, req) => PlayAlbumTrack(req.TrackId, req.PositionSeconds);
        albumWorldRuntime.AddToQueueRequested += (_, trackId) => QueueAlbumTrack(trackId);
        albumWorldRuntime.PauseRequested += (_, _) => { engine.Pause(); NotifySharedPlayPlaybackChanged("pause"); UpdateUiState(); };
        albumWorldRuntime.ResumeRequested += (_, _) => { engine.Play(); NotifySharedPlayPlaybackChanged("play"); UpdateUiState(); };
        albumWorldRuntime.SeekRequested += (_, pos) => { engine.Seek((float)pos); NotifySharedPlayPlaybackChanged("seek"); UpdateUiState(); };
        albumWorldRuntime.ExitWorldRequested += (_, _) => UnloadAlbumCapsule();
    }

    private void InitializeAlbumWorldUi()
    {
        albumWorldFallbackControl = new AlbumWorldFallbackControl
        {
            Dock = DockStyle.Fill,
            Visible = false
        };

        albumWorldFallbackControl.TrackSelected += (_, trackId) => PlayAlbumTrack(trackId, 0);

        if (embeddedContentControl is not null)
        {
            var parent = embeddedContentControl.Parent;
            parent?.Controls.Add(albumWorldFallbackControl);
            albumWorldFallbackControl.BringToFront();
        }
    }

    public async Task OpenAlbumCapsuleAsync(string path)
    {
        SpectralisLog.Info($"[.spectral] Opening: {Path.GetFileName(path)}");
        SetLoadingStatus("Reading album capsule…");

        AlbumCapsulePackage package;
        try
        {
            package = AlbumCapsuleReader.Read(path);
            SpectralisLog.Info($"[.spectral] Read OK — id={package.Manifest.Id} fingerprint={package.Fingerprint[..8]}…");
        }
        catch (InvalidDataException ex)
        {
            SpectralisLog.Error($"[.spectral] Invalid file: {ex.Message}");
            ShowError($"Could not open album capsule:\n\n{ex.Message}", "Album Capsule Error");
            return;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SpectralisLog.Error($"[.spectral] Read error: {ex.Message}");
            ShowError($"Could not read album capsule: {ex.Message}", "Album Capsule Error");
            return;
        }

        // Trust check: use cached metadata when valid, but refresh stale cache entries before denying.
        CreatorKeyMetadata? keyMeta = trustStore.GetCachedMetadata(package.Fingerprint);
        var keyMetaFromCache = keyMeta is not null;
        if (keyMeta is null)
        {
            SpectralisLog.Info($"[.spectral] Key not cached — fetching from CDN…");
            SetLoadingStatus("Verifying album key…");
            keyMeta = await FetchAlbumCreatorKeyFromCdnAsync(package.Fingerprint, package);
            if (keyMeta is null)
                return;
        }
        else
        {
            SpectralisLog.Info($"[.spectral] Key served from cache — creator={keyMeta.DisplayName} active={keyMeta.IsActive}");
        }

        if (keyMeta is null)
        {
            SpectralisLog.Warn($"[.spectral] Key not found on CDN");
            ShowError("This album capsule's signing key is not registered on the CDN.", "Album Capsule Error");
            package.Dispose();
            return;
        }

        if (keyMetaFromCache && !keyMeta.IsActive)
        {
            SpectralisLog.Info($"[.spectral] Cached key inactive — refreshing from CDN…");
            SetLoadingStatus("Refreshing album key…");
            keyMeta = await FetchAlbumCreatorKeyFromCdnAsync(package.Fingerprint, package);
            if (keyMeta is null)
                return;
            keyMetaFromCache = false;
        }

        if (!keyMeta.IsActive)
        {
            SpectralisLog.Warn($"[.spectral] Key revoked — creator={keyMeta.DisplayName}");
            ShowError($"Capsule rejected: The creator key for '{keyMeta.DisplayName}' has been revoked.", "Album Capsule Error");
            package.Dispose();
            return;
        }

        var deniedCapabilities = package.Manifest.Capabilities
            .Where(c => !keyMeta.AllowedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (deniedCapabilities.Count > 0 && keyMetaFromCache)
        {
            SpectralisLog.Info($"[.spectral] Cached key missing capabilities ({string.Join(", ", deniedCapabilities)}) — refreshing from CDN…");
            SetLoadingStatus("Refreshing album permissions…");
            keyMeta = await FetchAlbumCreatorKeyFromCdnAsync(package.Fingerprint, package);
            if (keyMeta is null)
                return;

            if (!keyMeta.IsActive)
            {
                SpectralisLog.Warn($"[.spectral] Key revoked after refresh — creator={keyMeta.DisplayName}");
                ShowError($"Capsule rejected: The creator key for '{keyMeta.DisplayName}' has been revoked.", "Album Capsule Error");
                package.Dispose();
                return;
            }

            deniedCapabilities = package.Manifest.Capabilities
                .Where(c => !keyMeta.AllowedCapabilities.Contains(c, StringComparer.OrdinalIgnoreCase))
                .ToList();
        }

        if (deniedCapabilities.Count > 0)
        {
            SpectralisLog.Warn($"[.spectral] Capability denied: {string.Join(", ", deniedCapabilities)}");
            ShowError($"Capsule rejected: Creator not authorized for {string.Join(", ", deniedCapabilities)}.", "Album Capsule Error");
            package.Dispose();
            return;
        }

        trustStore.CacheMetadata(package.Fingerprint, keyMeta);

        if (!trustStore.IsTrusted(package.Fingerprint))
        {
            SpectralisLog.Info($"[.spectral] Showing trust prompt for creator={keyMeta.DisplayName}");
            var trusted = ShowTrustPrompt(keyMeta);
            if (!trusted)
            {
                SpectralisLog.Info($"[.spectral] User denied trust");
                package.Dispose();
                return;
            }

            trustStore.Trust(package.Fingerprint, keyMeta.DisplayName);
            SpectralisLog.Info($"[.spectral] User trusted creator={keyMeta.DisplayName}");
        }

        await LoadAlbumCapsuleAsync(package, keyMeta);
    }

    private async Task<CreatorKeyMetadata?> FetchAlbumCreatorKeyFromCdnAsync(
        string fingerprint,
        AlbumCapsulePackage package)
    {
        using var cdnClient = new CapsuleCdnClient();
        try
        {
            var keyMeta = await cdnClient.FetchCreatorKeyAsync(fingerprint, CancellationToken.None);
            SpectralisLog.Info($"[.spectral] CDN key OK — creator={keyMeta?.DisplayName ?? "null"} active={keyMeta?.IsActive}");
            if (keyMeta is null)
            {
                SpectralisLog.Warn($"[.spectral] Key not found on CDN");
                ShowError("This album capsule's signing key is not registered on the CDN.", "Album Capsule Error");
                package.Dispose();
            }
            return keyMeta;
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] CDN unreachable: {ex.Message}");
            ShowError($"Could not refresh this album capsule's creator key from the CDN:\n\n{ex.Message}", "Album Capsule Error");
            package.Dispose();
            return null;
        }
    }

    private async Task LoadAlbumCapsuleAsync(AlbumCapsulePackage package, CreatorKeyMetadata keyMeta)
    {
        if (albumWorldCacheStore is null || albumWorldRuntime is null)
            return;

        UnloadAlbumCapsule();

        SetLoadingStatus("Preparing album world…");

        string albumDir;
        try
        {
            albumDir = albumWorldCacheStore.GetOrExtract(package);
            SpectralisLog.Info($"[.spectral] Album dir ready: {albumDir}");
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] Extraction failed: {ex.Message}");
            ShowError($"Could not extract album capsule:\n\n{ex.Message}", "Album Capsule Error");
            return;
        }

        package.Dispose();

        activeAlbumDir = albumDir;
        var session = AlbumWorldSessionStore.Load(albumDir);
        session.AlbumId = package.Manifest.Id;
        albumWorldRuntime.Load(package.Manifest, albumDir, session);
        albumWorldCacheStore.TouchAccess(package.Manifest.Id);

        var manifest = package.Manifest;
        var hasIntro = IsExplainerStory(manifest.Story) &&
            (activeCapsuleStory is null) &&
            !session.IntroCompleted;

        if (hasIntro)
        {
            var storyDoc = BuildCapsuleStoryFromManifest(manifest, albumDir);
            if (storyDoc is not null)
            {
                capsuleStoryControl?.LoadStory(storyDoc);
                ApplyCapsuleStoryVisibility(showStory: true);
                engine.Pause();
                capsuleStoryControl?.Focus();

                capsuleStoryPendingAutoPlay = false;
                activeCapsuleStory = storyDoc;
                return;
            }
        }

        ShowAlbumWorld();
        await Task.CompletedTask;
    }

    private void ShowAlbumWorld()
    {
        if (albumWorldRuntime?.Manifest is null)
            return;

        albumWorldShowingWorld = true;
        var manifest = albumWorldRuntime.Manifest;
        var session = albumWorldRuntime.Session!;

        SpectralisLog.Info($"[.spectral] ShowAlbumWorld — id={manifest.Id} worldEntry={manifest.World?.Entry ?? "none"}");

        // If world entry is present, load it into the WebView2
        if (!string.IsNullOrWhiteSpace(manifest.World?.Entry) && activeAlbumDir is not null && embeddedContentControl is not null)
        {
            var worldFolder = Path.Combine(activeAlbumDir, "world");

            if (Directory.Exists(worldFolder) || File.Exists(Path.Combine(activeAlbumDir, manifest.World.Entry)))
            {
                // If world section has its own subfolder use it, otherwise serve from albumDir
                var serveFolder = Directory.Exists(worldFolder) ? worldFolder : activeAlbumDir;
                var entryFile = Directory.Exists(worldFolder)
                    ? Path.GetFileName(manifest.World.Entry)
                    : manifest.World.Entry;

                embeddedContentControl.WorldMessageReceived -= AlbumWorld_WebMessageReceived;
                embeddedContentControl.WorldMessageReceived += AlbumWorld_WebMessageReceived;

                embeddedContentControl.LoadWorldContent(serveFolder, entryFile);

                // Inject bootstrap stubs + state after navigation completes
                _ = InjectAlbumWorldBootstrapAsync();

                albumWorldFallbackControl?.Hide();
                embeddedContentControl.Show();
                embeddedContentControl.BringToFront();
                // World HTML owns the content panel; hide the regular visualizer and its nav bar
                visualizerControl.Visible = false;
                visualizerNavPanel.Visible = false;
                UpdateUiState();
                return;
            }
        }

        // Fallback: show simple tracklist
        if (albumWorldFallbackControl is not null)
        {
            albumWorldFallbackControl.LoadAlbum(manifest, session);
            albumWorldFallbackControl.ApplyTheme(themePalette);
            albumWorldFallbackControl.Show();
            albumWorldFallbackControl.BringToFront();
        }

        UpdateUiState();

        // If we have a saved position, resume it
        if (!string.IsNullOrWhiteSpace(session.CurrentTrackId) && appSettings.AutoPlayOnOpen)
            PlayAlbumTrack(session.CurrentTrackId, session.CurrentPositionSeconds);
    }

    private async Task InjectAlbumWorldBootstrapAsync()
    {
        if (albumWorldRuntime is null || embeddedContentControl is null)
            return;

        // Small delay to allow the page to load before injecting
        await Task.Delay(200);

        if (!IsAlbumWorldActive)
            return;

        var bootstrap = albumWorldRuntime.BuildBootstrapScript();
        var ready = albumWorldRuntime.BuildReadyScript();

        await embeddedContentControl.ExecuteWorldScriptAsync(bootstrap);
        await embeddedContentControl.ExecuteWorldScriptAsync(ready);
    }

    private void PlayAlbumTrack(string trackId, double positionSeconds)
    {
        if (albumWorldRuntime is null || !IsAlbumWorldActive)
        {
            SpectralisLog.Warn($"[.spectral] Ignored play request while album world inactive — trackId={trackId}");
            return;
        }

        SpectralisLog.Info($"[.spectral] Play request — trackId={trackId} position={positionSeconds:0.###}");
        AudioTrackInfo? trackInfo;
        string? reactivePath;
        try
        {
            (trackInfo, reactivePath) = albumWorldRuntime.BuildTrackAssets(trackId);
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] Could not build track assets for {trackId}: {ex}");
            ShowError($"Could not prepare album track:\n\n{ex.Message}", "Album Playback Error");
            return;
        }

        if (trackInfo is null)
        {
            SpectralisLog.Warn($"[.spectral] Track assets not found — trackId={trackId}");
            return;
        }

        try
        {
            engine.Load(trackInfo.FilePath, trackInfo);
            visualizerControl.ClearFrame();
            trackBarSeek.Value = 0;
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] Could not load album track {trackId}: {ex}");
            ShowError($"Could not load track:\n\n{ex.Message}", "Album Playback Error");
            return;
        }

        if (reactivePath is not null)
        {
            try
            {
                var json = File.ReadAllText(reactivePath);
                var doc = JsonSerializer.Deserialize<ReactiveTimelineDocument>(json);
                LoadReactiveDocument(doc, reactivePath);
            }
            catch { }
        }
        else
        {
            UnloadReactive();
        }

        if (positionSeconds > 0)
            engine.Seek((float)positionSeconds);

        EnterAlbumTrackView(trackInfo);

        engine.Play();
        SpectralisLog.Info($"[.spectral] Playing album track — title={trackInfo.DisplayName} file={trackInfo.FilePath}");

        albumWorldRuntime.NotifyTrackStarted(trackId, positionSeconds);
        albumWorldCacheStore?.TouchAccess(albumWorldRuntime.Manifest!.Id);
        albumWorldRuntime.SaveSession();

        albumWorldFallbackControl?.HighlightTrack(trackId);
        NotifySharedPlayPlaybackChanged("album-track-play");
        UpdateUiState();

        _ = PushTrackChangedToWorldAsync(trackId);
    }

    private async Task PushTrackChangedToWorldAsync(string trackId)
    {
        if (albumWorldRuntime is null || embeddedContentControl is null)
            return;

        await Task.Delay(50);
        var script = albumWorldRuntime.BuildTrackChangedScript(trackId);
        if (!string.IsNullOrWhiteSpace(script))
            await embeddedContentControl.ExecuteWorldScriptAsync(script);
    }

    private void QueueAlbumTrack(string trackId)
    {
        if (albumWorldRuntime is null || !IsAlbumWorldActive)
            return;

        var (trackInfo, _) = albumWorldRuntime.BuildTrackAssets(trackId);
        if (trackInfo is null)
            return;

        queue.Add(trackInfo.FilePath);
        SyncQueueControl();
    }

    private void TickAlbumWorld()
    {
        if (!IsAlbumWorldActive || albumWorldRuntime is null)
            return;

        var position = engine.GetPosition();
        var playing = engine.IsPlaying;
        albumWorldRuntime.Tick(position, playing);

        // Detect track completion
        var length = engine.GetLength();
        var reachedEnd = length > 0 && position >= Math.Max(0, length - 0.25f);

        if (prevEngineIsPlaying && !playing && engine.IsLoaded && reachedEnd)
        {
            var completedTrackId = albumWorldRuntime.CurrentTrackId;
            if (completedTrackId is not null)
            {
                albumWorldRuntime.NotifyTrackCompleted(completedTrackId);
                albumWorldRuntime.SaveSession();
                albumWorldFallbackControl?.UpdateSession(albumWorldRuntime.Session!);
                ShowAlbumWorld();
            }
        }
    }

    private async Task PushTrackCompletedToWorldAsync(string trackId)
    {
        if (albumWorldRuntime is null || embeddedContentControl is null)
            return;

        var script = albumWorldRuntime.BuildTrackCompletedScript(trackId);
        if (!string.IsNullOrWhiteSpace(script))
            await embeddedContentControl.ExecuteWorldScriptAsync(script);
    }

    public async Task SyncAlbumWorldFrameAsync(VisualizerFrame frame, bool playing, float position)
    {
        if (!IsAlbumWorldActive || albumWorldRuntime is null || embeddedContentControl is null)
            return;

        var script = albumWorldRuntime.BuildFrameScript(frame, playing, position);
        await embeddedContentControl.SyncWorldFrame(frame, playing, position, script);
    }

    private void AlbumWorld_WebMessageReceived(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (albumWorldRuntime is null)
            return;

        try
        {
            string json;
            try
            {
                json = e.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                json = e.WebMessageAsJson;
            }

            if (!string.IsNullOrWhiteSpace(json))
            {
                SpectralisLog.Info($"[.spectral] World message — {json}");
                albumWorldRuntime.HandleWorldMessage(json);
            }
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] World message failed: {ex.Message}");
        }
    }

    private void CompleteAlbumWorldIntro()
    {
        if (albumWorldRuntime?.Session is not null)
        {
            albumWorldRuntime.Session.IntroCompleted = true;
            albumWorldRuntime.SaveSession();
        }

        ApplyCapsuleStoryVisibility(showStory: false);
        activeCapsuleStory = null;
        ShowAlbumWorld();
    }

    private static CapsuleStoryDocument? BuildCapsuleStoryFromManifest(AlbumManifest manifest, string albumDir)
    {
        if (!IsExplainerStory(manifest.Story))
            return null;

        var pages = manifest.Story.Pages.Count > 0
            ? manifest.Story.Pages
            : manifest.Story.Chapters;

        var defaultImageEntry = FirstNonBlank(
            manifest.Story.ExplainerImage,
            manifest.Story.CharacterImage,
            manifest.Story.ImageEntry,
            manifest.Story.Image,
            "assets/images/character.png");

        var scenes = new List<CapsuleStoryScene>();
        foreach (var page in pages.Select(ParseStoryPage))
        {
            var text = FirstNonBlank(page.Text, page.Title);
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var imageEntry = FirstNonBlank(
                page.ExplainerImage, page.CharacterImage, page.ImageEntry,
                page.Image, page.Portrait, page.Sprite, defaultImageEntry);
            var imageBytes = TryReadDiskPng(albumDir, imageEntry);
            scenes.Add(new CapsuleStoryScene(
                Speaker: FirstNonBlank(page.Speaker, manifest.Artist, "Story"),
                Text: text.Trim(),
                ImageBytes: imageBytes,
                ImageName: string.IsNullOrWhiteSpace(imageEntry) ? null : imageEntry.Trim()));
        }

        if (scenes.Count == 0)
        {
            var fallbackText = FirstNonBlank(manifest.Story.Backstory, manifest.Story.Summary);
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                scenes.Add(new CapsuleStoryScene(
                    Speaker: FirstNonBlank(manifest.Artist, "Story"),
                    Text: fallbackText.Trim(),
                    ImageBytes: TryReadDiskPng(albumDir, defaultImageEntry),
                    ImageName: string.IsNullOrWhiteSpace(defaultImageEntry) ? null : defaultImageEntry.Trim()));
            }
        }

        return scenes.Count > 0 ? new CapsuleStoryDocument(scenes) : null;
    }

    private static byte[]? TryReadDiskPng(string baseDir, string imageEntry)
    {
        if (string.IsNullOrWhiteSpace(imageEntry) ||
            !imageEntry.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            return null;

        var path = Path.GetFullPath(Path.Combine(baseDir, imageEntry.Trim().Replace('\\', '/')));
        if (!path.StartsWith(Path.GetFullPath(baseDir), StringComparison.OrdinalIgnoreCase))
            return null;

        try { return File.Exists(path) ? File.ReadAllBytes(path) : null; }
        catch { return null; }
    }

    private void UnloadAlbumCapsule()
    {
        if (albumWorldRuntime is not null && albumWorldRuntime.IsActive)
        {
            albumWorldRuntime.SaveSession();
            albumWorldRuntime.Unload();
        }

        if (embeddedContentControl is not null)
            embeddedContentControl.WorldMessageReceived -= AlbumWorld_WebMessageReceived;

        albumWorldFallbackControl?.Hide();
        albumWorldFallbackControl?.Clear();

        albumWorldShowingWorld = false;
        activeAlbumDir = null;
        UnloadReactive();
        UpdateEmbeddedContent(engine.CurrentTrack, force: true);
        UpdateUiState();
    }

    private void DisposeAlbumWorld()
    {
        UnloadAlbumCapsule();
        albumWorldRuntime?.Dispose();
        albumWorldRuntime = null;

        if (albumWorldFallbackControl is not null)
        {
            albumWorldFallbackControl.Parent?.Controls.Remove(albumWorldFallbackControl);
            albumWorldFallbackControl.Dispose();
            albumWorldFallbackControl = null;
        }
    }

    private void SetAlbumPinned(bool pinned)
    {
        if (albumWorldRuntime?.Manifest?.Id is not { } albumId)
            return;

        albumWorldCacheStore?.SetPinned(albumId, pinned);
    }

    private async void ClearCachedAlbumState()
    {
        if (albumWorldCacheStore is null)
            return;

        var result = MessageBox.Show(
            this,
            "Clear cached album progress, unlock state, bookmarks, and current positions from this device?",
            "Clear Cached Album State",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
            return;

        try
        {
            var resetActiveAlbum = IsAlbumWorldActive;
            var clearedSessions = albumWorldCacheStore.ClearAllState();
            ResetActiveAlbumSession();

            if (resetActiveAlbum && embeddedContentControl is not null)
            {
                await embeddedContentControl.ClearWorldStorageAndReloadAsync();
                await InjectAlbumWorldBootstrapAsync();
            }

            SpectralisLog.Info($"[.spectral] Cleared cached album state — sessions={clearedSessions}");
            MessageBox.Show(
                this,
                "Cached album state has been cleared.",
                "Clear Cached Album State",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectral] Could not clear cached album state: {ex}");
            MessageBox.Show(
                this,
                $"Spectralis could not clear cached album state.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Clear Cached Album State",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void ResetActiveAlbumSession()
    {
        if (albumWorldRuntime?.Manifest is null || activeAlbumDir is null)
            return;

        var manifest = albumWorldRuntime.Manifest;
        var session = new AlbumWorldSession
        {
            AlbumId = manifest.Id
        };

        engine.Unload();
        prevEngineIsPlaying = false;
        visualizerControl.ClearFrame();
        trackBarSeek.Value = 0;
        albumWorldRuntime.Load(manifest, activeAlbumDir, session);
        albumWorldRuntime.SaveSession();
        albumWorldFallbackControl?.UpdateSession(session);

        ShowAlbumWorld();
    }

    private void EnterAlbumTrackView(AudioTrackInfo trackInfo)
    {
        albumWorldShowingWorld = false;
        albumWorldFallbackControl?.Hide();
        UpdateEmbeddedContent(trackInfo, force: true);

        if (embeddedContentControl is { HasContent: true })
        {
            embeddedContentControl.Show();
            embeddedContentControl.BringToFront();
        }
        else
        {
            embeddedContentControl?.Hide();
            visualizerControl.Visible = true;
            visualizerNavPanel.Visible = true;
            visualizerControl.BringToFront();
        }
    }
}
