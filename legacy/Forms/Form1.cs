using System.Drawing;
using System.IO;
using System.Reflection;

namespace Spectralis;

public partial class Form1 : Form
{
    private readonly AudioEngine engine = new();
    private readonly WindowsNowPlayingService nowPlaying = new();
    private readonly DiscordRichPresenceService discordRichPresence = new();
    private readonly SharedPlaySessionController sharedPlay = new();
    private readonly SharedPlayCdnClient sharedPlayReceiverClient = new();
    private readonly SharedPlayJoinedPackageStore sharedPlayJoinedPackageStore = new();
    private readonly RedeemableVisualizerService redeemableVisualizers = new();
    private readonly string? startupPath;
    private readonly SharedPlayJoinRequest? startupSharedPlayJoin;
    private readonly ExternalOpenRequest? startupExternalOpenRequest;

    private AppSettings appSettings;
    private ThemePalette themePalette;
    private ThemeMode appliedThemeMode;
    private ThemeAccent appliedThemeAccent;
    private bool hasAppliedTheme;
    private bool isUpdatingSeekBar;
    private bool isApplyingSettings;
    private bool showRemainingTime;
    private bool isMuted;
    private float preMuteVolume = 0.85f;
    private AudioTrackInfo? displayedArtworkTrack;
    private Image? visualizerAlbumArt;
    private long nextVisualizerCycleTick;
    private long nextSharedPlayPulseTick;
    private long nextSharedPlayQueuePullTick;
    private long nextJoinedSharedPlayPollTick;
    private long nextJoinedSharedPlaySyncTick;
    private EmbeddedContentControl? embeddedContentControl;
    private CapsuleStoryControl? capsuleStoryControl;
    private AudioTrackInfo? displayedEmbeddedContentTrack;
    private bool displayedEmbeddedContentEnabled;
    private EmbeddedHtmlContext? activeInstalledHtmlVisualizer;
    private string? activeInstalledHtmlVisualizerId;
    private string? displayedInstalledHtmlVisualizerId;
    private bool updateNoticeChecked;
    private bool isP2wMode;
    private Panel? pnlP2w;
    private const int P2wSignMargin = 18;
    private const int P2wSignWidth = 820;
    private const int P2wSignHeight = 240;
    private const int P2wSignMinHeight = 120;
    private const string P2wBannerText = "PAID SKIP - PAY 2 WIN";
    private bool isJoiningSharedPlay;
    private bool isPollingJoinedSharedPlay;
    private bool isApplyingJoinedSharedPlaySync;
    private string? joinedSharedPlayStatus;
    private SharedPlayRemoteSession? joinedSharedPlaySession;
    private SharedPlayPlaybackSnapshot? joinedSharedPlayPlayback;
    private CancellationTokenSource? joinedSharedPlayCancellation;

    public Form1(
        string? startupPath = null,
        SharedPlayJoinRequest? startupSharedPlayJoin = null,
        ExternalOpenRequest? startupExternalOpenRequest = null)
    {
        this.startupPath = startupPath;
        this.startupSharedPlayJoin = startupSharedPlayJoin;
        this.startupExternalOpenRequest = startupExternalOpenRequest;
        appSettings = AppSettingsStore.Load();
        themePalette = ThemePalette.Create(appSettings.ThemeMode, appSettings.ThemeAccent);
        engine.DeviceRecoveryFailed += (_, _) => ShowAudioDeviceError();

        DoubleBuffered = true;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        InitializeComponent();
        ClientSizeChanged += (_, _) => PositionP2wSign();
        RestoreWindowPlacement();
        var appVersion = GetCurrentAppVersion();
        if (!string.IsNullOrWhiteSpace(appVersion))
            toolStripVersionLabel.Text = "v" + appVersion;
        InitializeCapsuleStoryUi();
        InitializeOpenUrlMenu();        // File menu: Open URL
        redeemableVisualizers.Reload();
        InitializeSharedPlay();
        InitializeClipboardMonitor();
        InitializeCapsule();
        InitializeReactive();
        InitializeAlbumWorld();
        // Library menu (order determines item order)
        InitializeLibrary();
        InitializePlaylists();
        InitializeTagEditor();
        InitializeScrobbling();
        // Tools menu group 1 — audio processing
        InitializeEffects();
        InitializeKaraoke();
        InitializeScriptedVisualizers();
        // Tools menu group 2 — utilities (BeatGrid adds Analyze to Library + Metronome to Tools)
        InitializeBeatGrid();
        InitializeLyricsTimingStudioMenu();
        // Tools menu group 3 — integrations
        InitializeObs();
        InitializeSongWars();
        NormalizeWorkflowMenus();
        StartExternalOpenIpcServer();
        Shown += (_, _) => ShowPostUpdateNoticeIfNeeded();
        HandleCreated += async (_, _) =>
        {
            ApplyTheme();
            InitializeNowPlaying();
            await InitializeEmbeddedContentAsync();
            InitializeAlbumWorldUi();
            InitializeYouTubeVideoUi();
            await InitializeSpotifyAsync();
            soundCloudPrewarmTask = InitializeSoundCloudAsync();
            sunoPrewarmTask = InitializeSunoAsync();
        };
        nextVisualizerCycleTick = Environment.TickCount64 + (long)VisualizerAutoCycleInterval.TotalMilliseconds;
        ApplyTheme();
        PopulateSettings();
        WireFileDrop(this);
        UpdateUiState();
    }

    private void ToggleP2wMode()
    {
        isP2wMode = !isP2wMode;
        fileP2wModeToolStripMenuItem.Checked = isP2wMode;

        if (isP2wMode)
        {
            pnlP2w = new Panel
            {
                BackColor = Color.FromArgb(255, 200, 0),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
            };

            var label = new Label
            {
                Text = P2wBannerText,
                ForeColor = Color.FromArgb(30, 20, 0),
                BackColor = Color.Transparent,
                Font = new Font("Segoe UI Semibold", 44f, FontStyle.Bold, GraphicsUnit.Point),
                Dock = DockStyle.Fill,
                Padding = new Padding(24, 28, 24, 34),
                TextAlign = ContentAlignment.MiddleCenter,
            };

            pnlP2w.Controls.Add(label);
            Controls.Add(pnlP2w);
            PositionP2wSign();
            pnlP2w.BringToFront();
        }
        else
        {
            if (pnlP2w is not null)
            {
                Controls.Remove(pnlP2w);
                pnlP2w.Dispose();
                pnlP2w = null;
            }
        }
    }

    private void PositionP2wSign()
    {
        if (pnlP2w is null)
            return;

        var availableWidth = Math.Max(1, ClientSize.Width - (P2wSignMargin * 2));
        var signWidth = Math.Min(P2wSignWidth, availableWidth);
        var availableHeight = Math.Max(
            P2wSignMinHeight,
            ClientSize.Height - menuStrip1.Bottom - statusStrip1.Height - (P2wSignMargin * 2));
        var signHeight = Math.Min(P2wSignHeight, Math.Max(P2wSignMinHeight, availableHeight / 3));

        pnlP2w.Size = new Size(signWidth, signHeight);
        pnlP2w.Left = Math.Max(P2wSignMargin, ClientSize.Width - signWidth - P2wSignMargin);
        pnlP2w.Top = menuStrip1.Bottom + P2wSignMargin;
        pnlP2w.PerformLayout();
        ResizeP2wSignText();
    }

    private void ResizeP2wSignText()
    {
        if (pnlP2w is null)
            return;

        foreach (var label in GetP2wSignLabels(pnlP2w))
        {
            FitP2wBannerLabel(label);
        }
    }

    private static void FitP2wBannerLabel(Label label)
    {
        var width = Math.Max(1, label.Width);
        var height = Math.Max(1, label.Height);
        var horizontalPadding = Math.Clamp(width / 34, 8, 24);
        var topPadding = Math.Clamp(height / 8, 8, 28);
        var bottomPadding = Math.Clamp(height / 7, 8, 34);
        label.Padding = new Padding(horizontalPadding, topPadding, horizontalPadding, bottomPadding);

        var available = new Size(
            Math.Max(1, width - label.Padding.Horizontal),
            Math.Max(1, height - label.Padding.Vertical));
        var preferredSize = Math.Clamp(Math.Min(width / 12.8f, height / 2.2f), 18f, 44f);
        // Scale down to fit but never below 18pt — let it clip at extreme narrow sizes rather than go tiny.
        var fontSize = FindP2wBannerFontSize(P2wBannerText, available, preferredSize, 34f, wrap: false);

        label.Text = P2wBannerText.ToUpperInvariant();
        label.Font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Point);
    }

    private static float FindP2wBannerFontSize(string text, Size available, float preferredSize, float minimumSize, bool wrap)
    {
        for (var size = preferredSize; size >= minimumSize; size -= 0.5f)
        {
            if (P2wBannerTextFits(text, size, available, wrap))
                return size;
        }

        return minimumSize;
    }

    private static bool P2wBannerTextFits(string text, float fontSize, Size available, bool wrap)
    {
        using var font = new Font("Segoe UI Semibold", fontSize, FontStyle.Bold, GraphicsUnit.Point);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
        var measured = wrap
            ? TextRenderer.MeasureText(text, font, available, flags | TextFormatFlags.WordBreak)
            : TextRenderer.MeasureText(text, font, new Size(10000, 10000), flags | TextFormatFlags.SingleLine);

        return measured.Width <= available.Width && measured.Height <= available.Height;
    }

    private static IEnumerable<Label> GetP2wSignLabels(Control root)
    {
        foreach (Control child in root.Controls)
        {
            if (child is Label label)
                yield return label;

            foreach (var nested in GetP2wSignLabels(child))
                yield return nested;
        }
    }

    private Color WindowBackColor => themePalette.WindowBackColor;
    private Color SurfaceBackColor => themePalette.SurfaceBackColor;
    private Color SurfaceAltBackColor => themePalette.SurfaceAltBackColor;
    private Color SurfaceRaisedColor => themePalette.SurfaceRaisedColor;
    private Color TextPrimaryColor => themePalette.TextPrimaryColor;
    private Color TextSecondaryColor => themePalette.TextSecondaryColor;
    private Color TextSoftColor => themePalette.TextSoftColor;
    private Color TextMutedColor => themePalette.TextMutedColor;
    private Color AccentPrimaryColor => themePalette.AccentPrimaryColor;
    private Color AccentSecondaryColor => themePalette.AccentSecondaryColor;
    private Color AccentSoftColor => themePalette.AccentSoftColor;
    private Color AccentContrastColor => themePalette.AccentContrastColor;
    private Color DangerColor => themePalette.DangerColor;
    private Color DangerTextColor => themePalette.DangerTextColor;
    private Color StatusBorderColor => themePalette.BorderStrongColor;
    private TimeSpan VisualizerAutoCycleInterval => TimeSpan.FromSeconds(appSettings.VisualizerCycleSeconds);

    private async void Form1_Load(object sender, EventArgs e)
    {
        if (startupSharedPlayJoin is not null)
        {
            await JoinSharedPlaySessionAsync(startupSharedPlayJoin);
            return;
        }

        if (startupExternalOpenRequest is not null)
        {
            await HandleExternalOpenRequestAsync(startupExternalOpenRequest);
            return;
        }

        if (string.IsNullOrWhiteSpace(startupPath))
            return;

        if (File.Exists(startupPath))
        {
            if (IsAlbumCapsulePath(startupPath))
            {
                _ = OpenAlbumCapsuleAsync(startupPath);
                return;
            }

            if (IsCapsulePath(startupPath))
            {
                _ = OpenCapsuleAsync(startupPath);
                return;
            }

            await HandleExternalFileOpenAsync(startupPath);
            return;
        }

        ShowError(
            $"The startup file could not be found:{Environment.NewLine}{Environment.NewLine}{startupPath}",
            "Open Error");
    }

    private async Task InitializeEmbeddedContentAsync()
    {
        embeddedContentControl = new EmbeddedContentControl
        {
            Visible = false,
            Dock = DockStyle.Fill
        };

        contentLayout.Controls.Add(embeddedContentControl, 0, 0);
        embeddedContentControl.BringToFront();
        WireFileDrop(embeddedContentControl);

        try
        {
            await embeddedContentControl.InitializeAsync();
            WireFileDrop(embeddedContentControl);
            UpdateEmbeddedContent(engine.CurrentTrack, force: true);
        }
        catch
        {
            // If WebView2 initialization fails, that's OK - the player still works with WASM visualizers
        }
    }

    private void InitializeCapsuleStoryUi()
    {
        capsuleStoryControl = new CapsuleStoryControl
        {
            Visible = false,
            Dock = DockStyle.Fill
        };

        capsuleStoryControl.ApplyTheme(themePalette);
        capsuleStoryControl.StoryCompleted += (_, _) =>
        {
            if (IsAlbumWorldActive)
                CompleteAlbumWorldIntro();
            else
                CompleteCapsuleStory();
        };
        contentLayout.Controls.Add(capsuleStoryControl, 0, 0);
        capsuleStoryControl.BringToFront();
        WireFileDrop(capsuleStoryControl);
    }

    private void ShowPostUpdateNoticeIfNeeded()
    {
        if (updateNoticeChecked)
            return;

        updateNoticeChecked = true;

        var pendingUpdateVersion = AppUpdateNoticeStore.ConsumePending();
        var currentVersion = GetCurrentAppVersion();
        if (string.IsNullOrWhiteSpace(currentVersion))
            currentVersion = pendingUpdateVersion ?? "";

        if (string.IsNullOrWhiteSpace(currentVersion))
            return;

        var previousVersion = appSettings.LastSeenAppVersion;
        var hasPendingUpdateNotice = !string.IsNullOrWhiteSpace(pendingUpdateVersion);
        if (string.Equals(previousVersion, currentVersion, StringComparison.OrdinalIgnoreCase))
        {
            if (hasPendingUpdateNotice)
                ShowUpdatedMessage();

            return;
        }

        appSettings.LastSeenAppVersion = currentVersion;
        SaveAppSettings();

        if (string.IsNullOrWhiteSpace(previousVersion) && !hasPendingUpdateNotice)
            return;

        ShowUpdatedMessage();
    }

    private void ShowUpdatedMessage()
    {
        MessageBox.Show(
            this,
            "Your version of Spectralis has been updated!",
            "Spectralis Updated",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private static string GetCurrentAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(Form1).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+')[0].Trim();

        return assembly.GetName().Version?.ToString() ?? "";
    }
}
