using System.IO;
using System.Text;
using System.Text.Json;

namespace Spectralis;

public partial class Form1
{
    private readonly CreatorTrustStore trustStore = new();
    private CapsuleTrustRuntime? capsuleTrustRuntime;
    private CapsulePackage? activeCapsule;
    private CapsuleStoryDocument? activeCapsuleStory;
    private bool capsuleStoryPendingAutoPlay;
    private string? capsuleTempAudioPath;

    private void InitializeCapsule()
    {
        trustStore.Load();
        capsuleTrustRuntime = new CapsuleTrustRuntime(trustStore);
    }

    public async Task OpenCapsuleAsync(string path)
    {
        if (capsuleTrustRuntime is null)
            return;

        SpectralisLog.Info($"[.spectralis] Opening: {Path.GetFileName(path)}");
        SetLoadingStatus("Opening capsule…");

        CapsuleOpenResult result;
        try
        {
            result = await capsuleTrustRuntime.OpenAsync(path, ShowTrustPrompt, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectralis] Open error: {ex.Message}");
            ShowError($"Failed to open capsule:\n\n{ex.Message}", "Capsule Error");
            return;
        }

        if (!result.IsSuccess)
        {
            SpectralisLog.Warn($"[.spectralis] Open failed — status={result.Status} msg={result.ErrorMessage}");
            var msg = result.Status switch
            {
                CapsuleOpenStatus.UserDenied    => null, // silent
                CapsuleOpenStatus.KeyRevoked    => $"Capsule rejected: {result.ErrorMessage}",
                CapsuleOpenStatus.KeyNotFound   => $"Capsule rejected: {result.ErrorMessage}",
                CapsuleOpenStatus.CapabilityDenied => $"Capsule rejected: {result.ErrorMessage}",
                CapsuleOpenStatus.SignatureInvalid  => $"This capsule has an invalid signature and cannot be opened.",
                CapsuleOpenStatus.NetworkError  => $"CDN unreachable and key is not cached:\n\n{result.ErrorMessage}",
                _                               => $"Could not open capsule: {result.ErrorMessage}"
            };

            if (msg is not null)
                ShowError(msg, "Capsule Error");
            return;
        }

        SpectralisLog.Info($"[.spectralis] Trust OK — creator={result.KeyMetadata!.DisplayName}");
        await LoadCapsuleAsync(result.Package!, result.KeyMetadata!);
    }

    private bool ShowTrustPrompt(CreatorKeyMetadata key)
    {
        if (InvokeRequired)
        {
            return (bool)Invoke(() => ShowTrustPrompt(key));
        }

        using var dialog = new CreatorTrustDialog(key, appSettings);
        dialog.ShowDialog(this);
        return dialog.Trusted;
    }

    private async Task LoadCapsuleAsync(CapsulePackage package, CreatorKeyMetadata keyMeta)
    {
        if (IsAlbumWorldActive)
            UnloadAlbumCapsule();
        UnloadCapsule();
        activeCapsule = package;

        SetLoadingStatus("Loading capsule audio…");

        var manifest = package.Manifest;
        SpectralisLog.Info($"[.spectralis] Loading — title={manifest.Title} audio={manifest.Audio.Entry}");

        // Extract audio to temp file
        var audioEntry = manifest.Audio.Entry;
        var audioBytes = package.TryReadEntry(audioEntry);
        if (audioBytes is null)
        {
            ShowError($"Capsule audio entry '{audioEntry}' not found.", "Capsule Error");
            UnloadCapsule();
            return;
        }

        var ext = Path.GetExtension(audioEntry);
        capsuleTempAudioPath = Path.Combine(Path.GetTempPath(), $"spectralis-capsule-{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(capsuleTempAudioPath, audioBytes);

        // Verify audio hash
        if (!VerifyAudioHash(audioBytes, manifest.Audio.Sha256))
        {
            SpectralisLog.Error($"[.spectralis] Audio hash mismatch — expected={manifest.Audio.Sha256[..8]}…");
            ShowError("Capsule audio file hash mismatch. The capsule may be corrupt.", "Capsule Error");
            UnloadCapsule();
            return;
        }

        // Build AudioTrackInfo from manifest
        var trackInfo = BuildCapsuleTrackInfo(manifest, capsuleTempAudioPath, package);
        activeCapsuleStory = BuildCapsuleStoryDocument(manifest, package);

        // Load into engine
        var loaded = false;
        try
        {
            engine.Load(capsuleTempAudioPath, trackInfo);
            visualizerControl.ClearFrame();
            trackBarSeek.Value = 0;
            loaded = true;
            SpectralisLog.Info($"[.spectralis] Engine loaded — hasHtml={trackInfo.EmbeddedHtml is not null} hasViz={trackInfo.EmbeddedVisualizer is not null}");
        }
        catch (Exception ex)
        {
            SpectralisLog.Error($"[.spectralis] Engine load error: {ex.Message}");
            ShowError($"Could not load capsule audio:\n\n{ex.Message}", "Capsule Error");
            UnloadCapsule();
            return;
        }

        // Load reactive timeline if present
        var reactiveJson = package.TryReadEntry("reactive.json");
        if (reactiveJson is not null)
        {
            try
            {
                var doc = JsonSerializer.Deserialize<ReactiveTimelineDocument>(reactiveJson);
                LoadReactiveDocument(doc, capsuleTempAudioPath);
            }
            catch { }
        }

        var shouldShowStory = loaded && activeCapsuleStory is not null;
        capsuleStoryPendingAutoPlay = shouldShowStory && appSettings.AutoPlayOnOpen;

        if (loaded && appSettings.AutoPlayOnOpen && !shouldShowStory)
        {
            engine.Play();
            NotifySharedPlayPlaybackChanged("capsule-load");
        }

        UpdateUiState();
        if (shouldShowStory)
        {
            ShowCapsuleStoryIfAvailable();
            BeginInvoke(new Action(ShowCapsuleStoryIfAvailable));
        }
    }

    private static AudioTrackInfo BuildCapsuleTrackInfo(
        CapsuleManifest manifest,
        string audioPath,
        CapsulePackage package)
    {
        var embeddedMetadata = AudioMetadataReader.Read(audioPath);

        byte[]? artBytes = embeddedMetadata.AlbumArtBytes;
        var artEntry = manifest.Assets.Images.FirstOrDefault();
        if (artEntry is not null)
            artBytes = package.TryReadEntry(artEntry) ?? artBytes;

        LyricsDocument? lyrics = embeddedMetadata.Lyrics;
        var lyricsEntry = manifest.Assets.Data
            .FirstOrDefault(d => d.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase));
        if (lyricsEntry is not null)
        {
            var lrcBytes = package.TryReadEntry(lyricsEntry);
            if (lrcBytes is not null)
            {
                try { lyrics = LrcParser.Parse(Encoding.UTF8.GetString(lrcBytes), "capsule"); }
                catch { }
            }
        }

        return new AudioTrackInfo(
            FilePath: audioPath,
            DisplayName: string.IsNullOrWhiteSpace(manifest.Title)
                ? embeddedMetadata.Title ?? Path.GetFileNameWithoutExtension(audioPath)
                : manifest.Title,
            Artist: string.IsNullOrWhiteSpace(manifest.Artist)
                ? embeddedMetadata.Artist
                : manifest.Artist,
            Album: string.IsNullOrWhiteSpace(manifest.Release.Album)
                ? embeddedMetadata.Album
                : manifest.Release.Album,
            AlbumArtBytes: artBytes,
            Lyrics: lyrics,
            EmbeddedVisualizer: embeddedMetadata.EmbeddedVisualizer,
            EmbeddedTheme: embeddedMetadata.EmbeddedTheme,
            EmbeddedHtml: TryBuildCapsuleHtmlContext(manifest, package) ?? embeddedMetadata.EmbeddedHtml,
            EmbeddedMarkdown: embeddedMetadata.EmbeddedMarkdown,
            EmbeddedVideo: embeddedMetadata.EmbeddedVideo,
            FormatName: "Spectralis Capsule",
            Channels: 2,
            SourceSampleRate: 44100,
            BitsPerSample: 16,
            Duration: TimeSpan.FromSeconds(manifest.Audio.DurationSeconds),
            SuppressAppLyrics: manifest.SuppressAppLyrics);
    }

    private static EmbeddedHtmlContext? TryBuildCapsuleHtmlContext(
        CapsuleManifest manifest,
        CapsulePackage package)
    {
        foreach (var vizEntry in manifest.Visualizers)
        {
            if (vizEntry is not JsonElement elem)
                continue;

            if (!TryGetJsonString(elem, "type", out var type) ||
                !string.Equals(type, "html", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryGetJsonString(elem, "binaryEntry", out var binaryEntry))
                continue;

            var htmlBytes = package.TryReadEntry(binaryEntry);
            if (htmlBytes is not { Length: > 0 })
                continue;

            TryGetJsonString(elem, "id", out var id);
            TryGetJsonString(elem, "version", out var version);

            // Binary assets (images etc.) keyed by binding name
            var binaryAssets = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            if (elem.TryGetProperty("binaryAssets", out var binaryAssetsElem) &&
                binaryAssetsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in binaryAssetsElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var entryPath = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(entryPath)) continue;
                    var bytes = package.TryReadEntry(entryPath);
                    if (bytes is { Length: > 0 })
                        binaryAssets[prop.Name] = bytes;
                }
            }

            // Read module descriptor to get internal dataRef IDs (e.g. "lyrics" → "somebody_better_lrc")
            // so the HTML's delta-data-json: references resolve correctly.
            var dataRefMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (TryGetJsonString(elem, "moduleEntry", out var moduleEntry))
            {
                var moduleBytes = package.TryReadEntry(moduleEntry);
                if (moduleBytes is not null)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(moduleBytes);
                        if (doc.RootElement.TryGetProperty("dataRefs", out var dataRefs) &&
                            dataRefs.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in dataRefs.EnumerateObject())
                            {
                                if (prop.Value.ValueKind == JsonValueKind.String)
                                    dataRefMap[prop.Name] = prop.Value.GetString() ?? "";
                            }
                        }
                    }
                    catch { }
                }
            }

            // Text/data assets: store under both binding name AND module's internal ref ID
            var textAssets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (elem.TryGetProperty("dataAssets", out var dataAssetsElem) &&
                dataAssetsElem.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in dataAssetsElem.EnumerateObject())
                {
                    if (prop.Value.ValueKind != JsonValueKind.String) continue;
                    var entryPath = prop.Value.GetString();
                    if (string.IsNullOrWhiteSpace(entryPath)) continue;
                    var bytes = package.TryReadEntry(entryPath);
                    if (bytes is null) continue;

                    var text = Encoding.UTF8.GetString(bytes);
                    textAssets[prop.Name] = text;

                    if (dataRefMap.TryGetValue(prop.Name, out var refId) && !string.IsNullOrWhiteSpace(refId))
                        textAssets[refId] = text;
                }
            }

            return new EmbeddedHtmlContext(
                string.IsNullOrWhiteSpace(id) ? "capsule-html" : id,
                htmlBytes,
                binaryAssets,
                textAssets,
                string.IsNullOrWhiteSpace(version) ? null : version);
        }

        return null;
    }

    private static bool TryGetJsonString(JsonElement elem, string property, out string value)
    {
        value = "";
        if (elem.TryGetProperty(property, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return !string.IsNullOrWhiteSpace(value);
        }

        return false;
    }

    private static CapsuleStoryDocument? BuildCapsuleStoryDocument(CapsuleManifest manifest, CapsulePackage package)
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
                page.ExplainerImage,
                page.CharacterImage,
                page.ImageEntry,
                page.Image,
                page.Portrait,
                page.Sprite,
                defaultImageEntry);
            var imageBytes = TryReadStoryPng(package, imageEntry);
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
                    ImageBytes: TryReadStoryPng(package, defaultImageEntry),
                    ImageName: string.IsNullOrWhiteSpace(defaultImageEntry) ? null : defaultImageEntry.Trim()));
            }
        }

        return scenes.Count > 0 ? new CapsuleStoryDocument(scenes) : null;
    }

    private static CapsuleStoryPage ParseStoryPage(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return new CapsuleStoryPage { Text = element.GetString() ?? "" };

        if (element.ValueKind != JsonValueKind.Object)
            return new CapsuleStoryPage();

        return new CapsuleStoryPage
        {
            Speaker = GetStoryString(element, "speaker", "character", "name"),
            Title = GetStoryString(element, "title"),
            Text = GetStoryString(element, "text", "body", "line", "content"),
            Image = GetStoryString(element, "image"),
            ImageEntry = GetStoryString(element, "imageEntry", "image_entry"),
            ExplainerImage = GetStoryString(element, "explainerImage", "explainer_image"),
            CharacterImage = GetStoryString(element, "characterImage", "character_image", "character"),
            Portrait = GetStoryString(element, "portrait"),
            Sprite = GetStoryString(element, "sprite")
        };
    }

    private static string GetStoryString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
                continue;

            if (property.ValueKind == JsonValueKind.String)
                return property.GetString() ?? "";
        }

        return "";
    }

    private static bool IsExplainerStory(CapsuleStory story)
    {
        static bool IsStoryTag(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim().ToLowerInvariant();
            return normalized is "story" or "story-mode" or "explainer" or "story-explainer" or "visual-novel" or "visual_novel" or "vn";
        }

        return IsStoryTag(story.Mode) ||
            IsStoryTag(story.Presentation) ||
            story.Tags.Any(IsStoryTag);
    }

    private static byte[]? TryReadStoryPng(CapsulePackage package, string imageEntry)
    {
        if (string.IsNullOrWhiteSpace(imageEntry) ||
            !imageEntry.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var trimmed = imageEntry.Trim().Replace('\\', '/');
        return package.TryReadEntry(trimmed) ??
            package.TryReadEntry(trimmed.TrimStart('/'));
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return "";
    }

    private void ShowCapsuleStoryIfAvailable()
    {
        if (activeCapsuleStory is null || capsuleStoryControl is null)
        {
            ApplyCapsuleStoryVisibility(showStory: false);
            return;
        }

        capsuleStoryControl.LoadStory(activeCapsuleStory);
        ApplyCapsuleStoryVisibility(showStory: true);
        engine.Pause();
        capsuleStoryControl.Focus();
    }

    private void ApplyCapsuleStoryVisibility(bool showStory)
    {
        if (capsuleStoryControl is null)
            return;

        capsuleStoryControl.Visible = showStory && capsuleStoryControl.HasStory;
        if (capsuleStoryControl.Visible)
        {
            embeddedContentControl?.Hide();
            visualizerControl.Visible = false;
            visualizerNavPanel.Visible = false;
            capsuleStoryControl.BringToFront();
            return;
        }

        UpdateEmbeddedContent(engine.CurrentTrack, force: true);
    }

    private void CompleteCapsuleStory()
    {
        ApplyCapsuleStoryVisibility(showStory: false);

        if (!capsuleStoryPendingAutoPlay || !engine.IsLoaded)
            return;

        capsuleStoryPendingAutoPlay = false;
        engine.Play();
        NotifySharedPlayPlaybackChanged("capsule-story-complete");
        UpdateUiState();
    }

    private static bool VerifyAudioHash(byte[] audioBytes, string expectedSha256)
    {
        if (string.IsNullOrWhiteSpace(expectedSha256))
            return true;

        var hash = System.Security.Cryptography.SHA256.HashData(audioBytes);
        var actual = Convert.ToHexString(hash).ToLowerInvariant();
        return string.Equals(actual, expectedSha256.ToLowerInvariant(), StringComparison.Ordinal);
    }

    private void UnloadCapsule()
    {
        activeCapsule?.Dispose();
        activeCapsule = null;
        activeCapsuleStory = null;
        capsuleStoryPendingAutoPlay = false;
        capsuleStoryControl?.Clear();
        UnloadReactive();

        if (capsuleTempAudioPath is not null)
        {
            try { File.Delete(capsuleTempAudioPath); } catch { }
            capsuleTempAudioPath = null;
        }
    }

    private void StopLocalPlaybackForExternalUrl()
    {
        if (IsAlbumWorldActive)
        {
            if (engine.IsLoaded)
                engine.Unload();

            UnloadAlbumCapsule();
            return;
        }

        if (activeCapsule is not null)
        {
            if (engine.IsLoaded)
                engine.Unload();

            UnloadCapsule();
            return;
        }

        if (engine.IsLoaded)
            engine.Stop();
    }

    private void DisposeCapsule()
    {
        UnloadCapsule();
        capsuleTrustRuntime?.Dispose();
        capsuleTrustRuntime = null;
    }
}
