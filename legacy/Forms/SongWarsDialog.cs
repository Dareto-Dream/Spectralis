using System.Drawing;
using System.IO;

namespace Spectralis;

internal sealed class SongWarsDialog : Form
{
    private readonly ThemePalette p;
    private readonly SongWarsTournamentStore store;
    private readonly Action<string?> playTrack;
    private SongWarsSessionController? controller;
    private Panel? activePanel;
    private string? pendingResultMatchId;

    // ── Browser panel ──────────────────────────────────────────────────────
    private Panel browserPanel = null!;
    private ListView lstTournaments = null!;
    private ModernButton btnLoadTournament = null!;
    private ModernButton btnDeleteTournament = null!;

    // ── Setup panel ────────────────────────────────────────────────────────
    private Panel setupPanel = null!;
    private TextBox txtTournamentName = null!;
    private ListView lstSubmissions = null!;
    private Label lblSubCount = null!;
    private Label lblSetupStatus = null!;
    private ModernButton btnCreateTournament = null!;
    private Panel judgeNamesPanel = null!;
    private readonly List<TextBox> judgeNameBoxes = [];
    private Label lblJudgeCount = null!;

    // ── Host panel ─────────────────────────────────────────────────────────
    private Panel hostPanel = null!;
    private TableLayoutPanel hostRoot = null!;
    private Label lblTournamentHeader = null!;
    private Label lblRoundStatus = null!;
    private Label lblMatchStatus = null!;
    private Label lblTrackATitle = null!;
    private Label lblTrackAArtist = null!;
    private Label lblTrackBTitle = null!;
    private Label lblTrackBArtist = null!;
    private Label lblFocusIndicator = null!;
    private ModernButton btnPlayA = null!;
    private ModernButton btnPlayB = null!;
    private ModernButton btnBeginA = null!;
    private ModernButton btnBeginB = null!;
    private ModernButton btnOpenVoting = null!;
    private ModernButton btnReveal = null!;
    private ModernButton btnSkip = null!;
    private ModernButton btnPauseResume = null!;
    private Panel voteSection = null!;
    private FlowLayoutPanel judgeVoteRows = null!;
    private Label lblTally = null!;
    private Panel resultSection = null!;
    private Label lblOutcomeTitle = null!;
    private Label lblOutcomeDetail = null!;
    private ModernButton btnNextMatch = null!;
    private TabControl hostTabs = null!;
    private SongWarsBracketView bracketView = null!;
    private ListView lstLog = null!;
    private int requestedVoteHeight;
    private int requestedResultHeight;
    private const int VoteSectionBaseHeight = 72;
    private const int VoteRowHeight = 50;
    private const int ResultSectionHeight = 130;
    private sealed record TournamentListRow(
        string TournamentId,
        string Name,
        DateTimeOffset CreatedAtUtc,
        int CompletedMatches,
        int TotalMatches,
        string Status);

    public SongWarsDialog(ThemePalette palette, SongWarsTournamentStore store, Action<string?> playTrack)
    {
        p = palette;
        this.store = store;
        this.playTrack = playTrack;

        SetupForm();
        browserPanel = BuildBrowserPanel();
        setupPanel = BuildSetupPanel();
        hostPanel = BuildHostPanel();

        Controls.Add(hostPanel);
        Controls.Add(setupPanel);
        Controls.Add(browserPanel);

        SwitchToPanel(browserPanel);
        Shown += async (_, _) => await LoadTournamentListAsync();
    }

    internal SongWarsTournament? CurrentTournament => controller?.Tournament;

    internal string? CurrentOverlayMatchId => pendingResultMatchId ?? controller?.CurrentMatch?.MatchId;

    // ── Form setup ─────────────────────────────────────────────────────────

    private void SetupForm()
    {
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        ClientSize          = new Size(960, 740);
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = false;
        MinimumSize = new Size(760, 560);
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Text = "Song Wars";
        BackColor = p.WindowBackColor;
        ForeColor = p.TextPrimaryColor;
        WindowChromeStyler.ApplyTheme(this, p);
        Resize += (_, _) => UpdateResponsiveHostLayout();
    }

    private void SwitchToPanel(Panel next)
    {
        if (activePanel is not null)
            activePanel.Visible = false;
        next.Visible = true;
        next.BringToFront();
        activePanel = next;
    }

    // ── Browser panel ──────────────────────────────────────────────────────

    private Panel BuildBrowserPanel()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 28, 32, 24),
            RowCount = 3,
            Visible = false
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        // Header row
        var header = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 20),
            RowCount = 1
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = MakeLabel("Song Wars", 24f, bold: true);
        var btnNew = MakePrimaryButton("New Tournament", 160);
        btnNew.Click += (_, _) => ShowSetupPanel();

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(btnNew, 1, 0);

        // Tournament list
        lstTournaments = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            OwnerDraw = false,
            View = View.Details,
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextPrimaryColor,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0, 0, 0, 12)
        };
        lstTournaments.Columns.Add("Name", 340);
        lstTournaments.Columns.Add("Created", 180);
        lstTournaments.Columns.Add("Matches", 120);
        lstTournaments.Columns.Add("Status", 220);
        lstTournaments.SelectedIndexChanged += (_, _) => UpdateBrowserButtons();
        lstTournaments.MouseDoubleClick += async (_, _) => await LoadSelectedTournamentAsync();

        // Footer buttons
        var footer = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        btnLoadTournament = MakePrimaryButton("Open Tournament", 164);
        btnLoadTournament.Enabled = false;
        btnLoadTournament.Click += async (_, _) => await LoadSelectedTournamentAsync();

        btnDeleteTournament = MakeGhostButton("Delete", 80, p.DangerColor);
        btnDeleteTournament.Enabled = false;
        btnDeleteTournament.Click += async (_, _) => await DeleteSelectedTournamentAsync();

        footer.Controls.Add(btnLoadTournament);
        footer.Controls.Add(btnDeleteTournament);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(lstTournaments, 0, 1);
        root.Controls.Add(footer, 0, 2);

        return root;
    }

    private async Task LoadTournamentListAsync()
    {
        lstTournaments.Items.Clear();
        lstTournaments.Items.Add(new ListViewItem("Loading tournaments..."));
        UpdateBrowserButtons();

        try
        {
            var rows = await Task.Run(LoadTournamentRows);
            if (IsDisposed) return;

            lstTournaments.Items.Clear();
            foreach (var row in rows)
            {
                var item = new ListViewItem(row.Name);
                item.SubItems.Add(row.CreatedAtUtc.LocalDateTime.ToString("MMM d, yyyy h:mm tt"));
                item.SubItems.Add($"{row.CompletedMatches}/{row.TotalMatches}");
                item.SubItems.Add(row.Status);
                item.Tag = row.TournamentId;
                lstTournaments.Items.Add(item);
            }
        }
        catch (Exception ex)
        {
            lstTournaments.Items.Clear();
            MessageBox.Show(this, $"Could not load tournaments:\n\n{ex.Message}", "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        UpdateBrowserButtons();
    }

    private List<TournamentListRow> LoadTournamentRows()
    {
        var rows = new List<TournamentListRow>();
        foreach (var id in store.ListTournamentIds())
        {
            var t = store.LoadTournamentAsync(id).GetAwaiter().GetResult();
            if (t is null) continue;

            var completedMatches = t.Matches.Count(m => m.Result != SongWarsOutcome.Pending);
            var status = t.CurrentMatchId is null
                ? (completedMatches > 0 ? "Complete" : "Not started")
                : GetCurrentMatchStatus(t);

            rows.Add(new TournamentListRow(
                t.TournamentId,
                t.Name,
                t.CreatedAtUtc,
                completedMatches,
                t.Matches.Count,
                status));
        }

        return rows
            .OrderByDescending(row => row.CreatedAtUtc)
            .ToList();
    }

    private static string GetCurrentMatchStatus(SongWarsTournament t)
    {
        var match = SongWarsBracketEngine.GetCurrentMatch(t);
        if (match is null) return "No current match";
        return $"{match.RoundId} | {match.Phase}";
    }

    private void UpdateBrowserButtons()
    {
        var hasSelection = lstTournaments.SelectedItems.Count > 0;
        btnLoadTournament.Enabled = hasSelection;
        btnDeleteTournament.Enabled = hasSelection;
    }

    private async Task LoadSelectedTournamentAsync()
    {
        if (lstTournaments.SelectedItems.Count == 0) return;
        var id = lstTournaments.SelectedItems[0].Tag as string;
        if (string.IsNullOrWhiteSpace(id)) return;
        await LoadTournamentByIdAsync(id);
    }

    private async Task LoadTournamentByIdAsync(string id)
    {
        btnLoadTournament.Enabled = false;
        btnLoadTournament.Text = "Opening...";

        SongWarsTournament? t = null;
        List<SongWarsVote> votes = [];
        try
        {
            (t, votes) = await Task.Run(() =>
            {
                var loaded = store.LoadTournamentAsync(id).GetAwaiter().GetResult();
                var loadedVotes = !string.IsNullOrWhiteSpace(loaded?.CurrentMatchId)
                    ? store.LoadVotesAsync(id, loaded.CurrentMatchId).GetAwaiter().GetResult()
                    : [];
                return (loaded, loadedVotes);
            });
        }
        finally
        {
            btnLoadTournament.Text = "Open Tournament";
            UpdateBrowserButtons();
        }

        if (t is null)
        {
            MessageBox.Show(this, "Could not load tournament.", "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var c = new SongWarsSessionController(t);

        // Reload votes for current match
        if (!string.IsNullOrWhiteSpace(t.CurrentMatchId))
        {
            c.LoadVotes(t.CurrentMatchId, votes);
        }

        controller = c;
        pendingResultMatchId = null;
        Text = $"Song Wars - {t.Name}";
        RefreshHostPanel();
        SwitchToPanel(hostPanel);
    }

    private async Task DeleteSelectedTournamentAsync()
    {
        if (lstTournaments.SelectedItems.Count == 0) return;
        var item = lstTournaments.SelectedItems[0];
        var id = item.Tag as string;
        if (string.IsNullOrWhiteSpace(id)) return;

        var result = MessageBox.Show(
            this,
            $"Delete tournament \"{item.Text}\"? This cannot be undone.",
            "Delete Tournament",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes) return;

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Spectralis", "SongWars", id);
            await Task.Run(() =>
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            });
            await LoadTournamentListAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not delete tournament:\n\n{ex.Message}", "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Setup panel ────────────────────────────────────────────────────────

    private Panel BuildSetupPanel()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(32, 24, 32, 24),
            RowCount = 10,
            Visible = false
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: nav + title
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: name field
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: submissions header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55F)); // 3: submissions list
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 4: sub action buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 5: judges header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45F)); // 6: judge names
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 7: judge action buttons
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 8: setup status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 9: create button

        // Row 0: navigation + title
        var navRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 20),
            RowCount = 1
        };
        navRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        navRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        navRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var btnBack = MakeGhostButton("< Back", 80);
        btnBack.Click += async (_, _) =>
        {
            SwitchToPanel(browserPanel);
            await LoadTournamentListAsync();
        };

        var setupTitle = MakeLabel("New Tournament", 20f, bold: true);
        navRow.Controls.Add(btnBack, 0, 0);
        navRow.Controls.Add(setupTitle, 1, 0);

        // Row 1: tournament name
        var nameRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            RowCount = 1
        };
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        nameRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        nameRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var nameLabel = MakeLabel("Tournament Name:", 9.5f, color: p.TextSecondaryColor);
        nameLabel.Margin = new Padding(0, 8, 12, 0);

        txtTournamentName = new TextBox
        {
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            BackColor = p.SurfaceBackColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 10f),
            ForeColor = p.TextPrimaryColor,
            Height = 32
        };
        txtTournamentName.TextChanged += (_, _) => UpdateSetupCreateButton();
        nameRow.Controls.Add(nameLabel, 0, 0);
        nameRow.Controls.Add(txtTournamentName, 1, 0);

        // Row 2: submissions header
        var subHeaderRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
            RowCount = 1
        };
        subHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        subHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        subHeaderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lblSubCount = MakeLabel("Submissions: 0 / 32", 9.5f, color: p.TextSecondaryColor);
        var subHint = MakeLabel("2-32 tracks", 8.5f, color: p.TextMutedColor);
        subHint.Anchor = AnchorStyles.Right;
        subHeaderRow.Controls.Add(lblSubCount, 0, 0);
        subHeaderRow.Controls.Add(subHint, 1, 0);

        // Row 3: submissions list
        lstSubmissions = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = true,
            View = View.Details,
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextPrimaryColor,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0, 0, 0, 6),
            ShowItemToolTips = true
        };
        lstSubmissions.Columns.Add("#", 36);
        lstSubmissions.Columns.Add("Title", 240);
        lstSubmissions.Columns.Add("Artist", 200);
        lstSubmissions.Columns.Add("File", 340);
        lstSubmissions.ItemActivate += (_, _) => PreviewSelectedSubmission();
        lstSubmissions.AllowDrop = true;
        lstSubmissions.DragEnter += OnSubmissionDragEnter;
        lstSubmissions.DragDrop += OnSubmissionDragDrop;

        // Row 4: submission buttons
        var subButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 14)
        };

        var btnAddFiles = MakePrimaryButton("Add Files...", 120);
        btnAddFiles.Click += (_, _) => AddSubmissionFiles();

        var btnRemoveSub = MakeGhostButton("Remove", 90, p.DangerColor);
        btnRemoveSub.Click += (_, _) => RemoveSelectedSubmissions();

        subButtons.Controls.Add(btnAddFiles);
        subButtons.Controls.Add(btnRemoveSub);

        // Row 5: judges header
        var judgeHeaderRow = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6),
            RowCount = 1
        };
        judgeHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        judgeHeaderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        judgeHeaderRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lblJudgeCount = MakeLabel("Judges: 1 / 5", 9.5f, color: p.TextSecondaryColor);
        var judgeHint = MakeLabel("1-5 judges", 8.5f, color: p.TextMutedColor);
        judgeHint.Anchor = AnchorStyles.Right;
        judgeHeaderRow.Controls.Add(lblJudgeCount, 0, 0);
        judgeHeaderRow.Controls.Add(judgeHint, 1, 0);

        // Row 6: judge name boxes
        judgeNamesPanel = new Panel
        {
            AutoScroll = true,
            BackColor = p.SurfaceBackColor,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 6)
        };
        judgeNamesPanel.Resize += (_, _) => ResizeJudgeNameBoxes();
        AddJudgeNameBox("Judge 1");

        // Row 7: judge buttons
        var judgeButtons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 16)
        };

        var btnAddJudge = MakeGhostButton("Add Judge", 100);
        btnAddJudge.Click += (_, _) => AddJudgeSlot();

        var btnRemoveJudge = MakeGhostButton("Remove", 90, p.DangerColor);
        btnRemoveJudge.Click += (_, _) => RemoveLastJudge();

        judgeButtons.Controls.Add(btnAddJudge);
        judgeButtons.Controls.Add(btnRemoveJudge);

        // Row 8: setup status
        lblSetupStatus = MakeLabel("", 9f, color: p.TextMutedColor);
        lblSetupStatus.AutoSize = false;
        lblSetupStatus.Dock = DockStyle.Fill;
        lblSetupStatus.Height = 24;
        lblSetupStatus.TextAlign = ContentAlignment.MiddleLeft;
        lblSetupStatus.Margin = new Padding(0, 0, 0, 8);

        // Row 9: create button
        btnCreateTournament = MakePrimaryButton("Create Tournament", 200);
        btnCreateTournament.Enabled = false;
        btnCreateTournament.Click += (_, _) => CreateTournament();
        btnCreateTournament.Anchor = AnchorStyles.Right;

        root.Controls.Add(navRow, 0, 0);
        root.Controls.Add(nameRow, 0, 1);
        root.Controls.Add(subHeaderRow, 0, 2);
        root.Controls.Add(lstSubmissions, 0, 3);
        root.Controls.Add(subButtons, 0, 4);
        root.Controls.Add(judgeHeaderRow, 0, 5);
        root.Controls.Add(judgeNamesPanel, 0, 6);
        root.Controls.Add(judgeButtons, 0, 7);
        root.Controls.Add(lblSetupStatus, 0, 8);
        root.Controls.Add(btnCreateTournament, 0, 9);

        return root;
    }

    private void ShowSetupPanel()
    {
        // Reset setup state
        playTrack(null);
        pendingResultMatchId = null;
        txtTournamentName.Text = "";
        lstSubmissions.Items.Clear();
        judgeNamesPanel.Controls.Clear();
        judgeNameBoxes.Clear();
        AddJudgeNameBox("Judge 1");
        UpdateSetupCreateButton();
        SwitchToPanel(setupPanel);
    }

    private void AddJudgeNameBox(string defaultName = "")
    {
        if (judgeNameBoxes.Count >= 5) return;

        var box = new TextBox
        {
            BackColor = p.SurfaceBackColor,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = p.TextPrimaryColor,
            Height = 28,
            Left = 8,
            Text = defaultName,
            Top = judgeNameBoxes.Count * 36 + 8,
            Width = judgeNamesPanel.Width > 0 ? judgeNamesPanel.Width - 16 : 400
        };
        box.TextChanged += (_, _) => UpdateSetupCreateButton();
        judgeNamesPanel.Controls.Add(box);
        judgeNameBoxes.Add(box);
        ResizeJudgeNameBoxes();
        UpdateJudgeCount();
    }

    private void ResizeJudgeNameBoxes()
    {
        var width = Math.Max(240, judgeNamesPanel.ClientSize.Width - 20);
        for (var i = 0; i < judgeNameBoxes.Count; i++)
        {
            judgeNameBoxes[i].Top = i * 36 + 8;
            judgeNameBoxes[i].Width = width;
        }
    }

    private void AddJudgeSlot()
    {
        if (judgeNameBoxes.Count >= 5) return;
        AddJudgeNameBox($"Judge {judgeNameBoxes.Count + 1}");
    }

    private void RemoveLastJudge()
    {
        if (judgeNameBoxes.Count <= 1) return;
        var last = judgeNameBoxes[^1];
        judgeNameBoxes.RemoveAt(judgeNameBoxes.Count - 1);
        judgeNamesPanel.Controls.Remove(last);
        last.Dispose();
        UpdateJudgeCount();
        UpdateSetupCreateButton();
    }

    private void UpdateJudgeCount()
    {
        lblJudgeCount.Text = $"Judges: {judgeNameBoxes.Count} / 5";
    }

    private void AddSubmissionFiles()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "Audio Files|*.mp3;*.flac;*.wav;*.ogg;*.aac;*.m4a;*.opus|All Files|*.*",
            Multiselect = true,
            Title = "Add Submissions"
        };

        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        AddSubmissionPaths(dlg.FileNames);
    }

    private void OnSubmissionDragEnter(object? sender, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;
    }

    private void OnSubmissionDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            AddSubmissionPaths(paths);
    }

    private void AddSubmissionPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths.Where(File.Exists))
        {
            if (lstSubmissions.Items.Count >= SongWarsBracketEngine.MaxSubmissionCount)
                break;

            if (lstSubmissions.Items.Cast<ListViewItem>().Any(item =>
                    string.Equals(item.Tag as string, path, StringComparison.OrdinalIgnoreCase)))
                continue;

            AddSubmissionPath(path);
        }

        RenumberSubmissions();
        UpdateSetupCreateButton();
    }

    private void AddSubmissionPath(string path)
    {
        var metadata = AudioMetadataReader.Read(path);
        var index = lstSubmissions.Items.Count + 1;
        var title = string.IsNullOrWhiteSpace(metadata.Title)
            ? Path.GetFileNameWithoutExtension(path)
            : metadata.Title.Trim();
        var artist = string.IsNullOrWhiteSpace(metadata.Artist)
            ? ""
            : metadata.Artist.Trim();

        var item = new ListViewItem(index.ToString());
        item.SubItems.Add(title);
        item.SubItems.Add(artist);
        item.SubItems.Add(path);
        item.Tag = path;
        item.ToolTipText = path;
        lstSubmissions.Items.Add(item);
    }

    private void PreviewSelectedSubmission()
    {
        if (lstSubmissions.SelectedItems.Count == 0) return;
        if (lstSubmissions.SelectedItems[0].Tag is string path)
            playTrack(path);
    }

    private void RemoveSelectedSubmissions()
    {
        var toRemove = lstSubmissions.SelectedItems.Cast<ListViewItem>().ToList();
        foreach (var item in toRemove)
            lstSubmissions.Items.Remove(item);

        RenumberSubmissions();
        UpdateSubmissionCount();
        UpdateSetupCreateButton();
    }

    private void RenumberSubmissions()
    {
        for (var i = 0; i < lstSubmissions.Items.Count; i++)
            lstSubmissions.Items[i].Text = (i + 1).ToString();
    }

    private void UpdateSubmissionCount()
    {
        var count = lstSubmissions.Items.Count;
        lblSubCount.Text = $"Submissions: {count} / {SongWarsBracketEngine.MaxSubmissionCount}";
        lblSubCount.ForeColor = count >= SongWarsBracketEngine.MinSubmissionCount
            ? p.AccentPrimaryColor
            : p.TextSecondaryColor;
    }

    private void UpdateSetupCreateButton()
    {
        UpdateSubmissionCount();
        UpdateJudgeCount();

        var nameOk = !string.IsNullOrWhiteSpace(txtTournamentName.Text);
        var count = lstSubmissions.Items.Count;
        var subOk = count >= SongWarsBracketEngine.MinSubmissionCount &&
                    count <= SongWarsBracketEngine.MaxSubmissionCount;
        var judgeOk = judgeNameBoxes.Count >= 1 && judgeNameBoxes.All(b => !string.IsNullOrWhiteSpace(b.Text));

        btnCreateTournament.Enabled = nameOk && subOk && judgeOk;
        lblSetupStatus.Text = (nameOk, subOk, judgeOk) switch
        {
            (false, _, _) => "Name the tournament to continue.",
            (_, false, _) => $"Add at least {SongWarsBracketEngine.MinSubmissionCount} tracks. Drag audio files here or use Add Files.",
            (_, _, false) => "Every judge needs a display name.",
            _ => "Ready to create. Double-click a submission to preview it."
        };
        lblSetupStatus.ForeColor = btnCreateTournament.Enabled
            ? p.AccentPrimaryColor
            : p.TextMutedColor;
    }

    private async void CreateTournament()
    {
        var name = txtTournamentName.Text.Trim();

        var submissions = lstSubmissions.Items
            .Cast<ListViewItem>()
            .Select((item, i) => new SongWarsSubmission
            {
                DisplayTitle = string.IsNullOrWhiteSpace(item.SubItems[1].Text)
                    ? $"Track {i + 1}"
                    : item.SubItems[1].Text.Trim(),
                ArtistDisplayName = string.IsNullOrWhiteSpace(item.SubItems[2].Text)
                    ? ""
                    : item.SubItems[2].Text.Trim(),
                LocalFilePath = item.Tag as string
            })
            .ToList();

        var judges = judgeNameBoxes
            .Select(b => new SongWarsJudge
            {
                DisplayName = b.Text.Trim()
            })
            .ToList();

        btnCreateTournament.Enabled = false;
        btnCreateTournament.Text = "Creating...";

        try
        {
            var t = await Task.Run(() => SongWarsBracketEngine.CreateTournament(name, submissions, judges));
            await store.SaveTournamentAsync(t);
            controller = new SongWarsSessionController(t);
            pendingResultMatchId = null;
            btnCreateTournament.Text = "Create Tournament";
            Text = $"Song Wars - {t.Name}";
            RefreshHostPanel();
            SwitchToPanel(hostPanel);
        }
        catch (Exception ex)
        {
            btnCreateTournament.Enabled = true;
            btnCreateTournament.Text = "Create Tournament";
            MessageBox.Show(this, $"Could not create tournament:\n\n{ex.Message}", "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── Host panel ─────────────────────────────────────────────────────────

    private Panel BuildHostPanel()
    {
        hostRoot = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 6
        };
        hostRoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));   // 0: header
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 152F));  // 1: match card
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 104F));  // 2: phase controls
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));    // 3: vote section (dynamic)
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Absolute, 0F));    // 4: result section (dynamic)
        hostRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 5: match log

        // Row 0: header
        var header = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 16, 24, 8),
            RowCount = 2
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.BackColor = p.SurfaceAltBackColor;

        var btnBack = MakeGhostButton("< Back", 80);
        btnBack.Click += async (_, _) =>
        {
            playTrack(null);
            SwitchToPanel(browserPanel);
            await LoadTournamentListAsync();
        };
        btnBack.Margin = new Padding(0, 0, 16, 0);

        lblTournamentHeader = MakeLabel("", 15f, bold: true);
        lblTournamentHeader.Dock = DockStyle.Fill;
        lblTournamentHeader.AutoSize = false;

        lblRoundStatus = MakeLabel("", 9.5f, color: p.TextSecondaryColor);
        lblRoundStatus.Dock = DockStyle.Fill;
        lblRoundStatus.AutoSize = false;

        lblMatchStatus = MakeLabel("", 9f, color: p.TextMutedColor);
        lblMatchStatus.Anchor = AnchorStyles.Right;

        header.Controls.Add(btnBack, 0, 0);
        header.Controls.Add(lblTournamentHeader, 1, 0);
        header.Controls.Add(lblMatchStatus, 2, 0);
        header.SetColumnSpan(lblRoundStatus, 2);
        header.Controls.Add(lblRoundStatus, 1, 1);

        // Row 1: match card
        var matchCard = new TableLayoutPanel
        {
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(24, 12, 24, 12),
            RowCount = 1
        };
        matchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        matchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        matchCard.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45F));
        matchCard.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        matchCard.BackColor = p.SurfaceBackColor;

        // Track A side
        var trackASide = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        trackASide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        trackASide.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackASide.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackASide.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblSlotA = MakeLabel("TRACK A", 7.5f, bold: true, color: p.TextMutedColor);
        lblSlotA.Margin = new Padding(0, 0, 0, 4);

        lblTrackATitle = MakeLabel("-", 13f, bold: true);
        lblTrackATitle.AutoEllipsis = true;
        lblTrackATitle.AutoSize = false;
        lblTrackATitle.Dock = DockStyle.Fill;
        lblTrackATitle.Margin = new Padding(0, 0, 0, 2);

        lblTrackAArtist = MakeLabel("", 9f, color: p.TextSecondaryColor);
        lblTrackAArtist.AutoEllipsis = true;
        lblTrackAArtist.AutoSize = false;
        lblTrackAArtist.Dock = DockStyle.Fill;

        trackASide.Controls.Add(lblSlotA, 0, 0);
        trackASide.Controls.Add(lblTrackATitle, 0, 1);
        trackASide.Controls.Add(lblTrackAArtist, 0, 2);

        // VS center
        var vsPanel = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        vsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        vsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        vsPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        vsPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var vsLabel = MakeLabel("VS", 16f, bold: true, color: p.TextMutedColor);
        vsLabel.TextAlign = ContentAlignment.MiddleCenter;
        vsLabel.Dock = DockStyle.Fill;
        vsLabel.AutoSize = false;

        lblFocusIndicator = MakeLabel("", 8f, color: p.AccentPrimaryColor);
        lblFocusIndicator.TextAlign = ContentAlignment.MiddleCenter;
        lblFocusIndicator.Dock = DockStyle.Fill;
        lblFocusIndicator.AutoSize = false;

        vsPanel.Controls.Add(new Label { Dock = DockStyle.Fill }, 0, 0);
        vsPanel.Controls.Add(vsLabel, 0, 1);
        vsPanel.Controls.Add(lblFocusIndicator, 0, 2);

        // Track B side
        var trackBSide = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 3
        };
        trackBSide.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        trackBSide.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackBSide.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        trackBSide.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var lblSlotB = MakeLabel("TRACK B  ON TRIAL", 7.5f, bold: true, color: p.AccentSoftColor);
        lblSlotB.Margin = new Padding(0, 0, 0, 4);

        lblTrackBTitle = MakeLabel("-", 13f, bold: true);
        lblTrackBTitle.AutoEllipsis = true;
        lblTrackBTitle.AutoSize = false;
        lblTrackBTitle.Dock = DockStyle.Fill;
        lblTrackBTitle.Margin = new Padding(0, 0, 0, 2);

        lblTrackBArtist = MakeLabel("", 9f, color: p.TextSecondaryColor);
        lblTrackBArtist.AutoEllipsis = true;
        lblTrackBArtist.AutoSize = false;
        lblTrackBArtist.Dock = DockStyle.Fill;

        trackBSide.Controls.Add(lblSlotB, 0, 0);
        trackBSide.Controls.Add(lblTrackBTitle, 0, 1);
        trackBSide.Controls.Add(lblTrackBArtist, 0, 2);

        matchCard.Controls.Add(trackASide, 0, 0);
        matchCard.Controls.Add(vsPanel, 1, 0);
        matchCard.Controls.Add(trackBSide, 2, 0);

        // Row 2: play + phase controls
        var controlsRow = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 10, 24, 10),
            RowCount = 2
        };
        controlsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        controlsRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        controlsRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        controlsRow.BackColor = p.SurfaceAltBackColor;

        var playbackRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        var phaseRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        btnPlayA = MakeGhostButton("Play A", 76);
        btnPlayA.Margin = new Padding(0, 0, 6, 0);
        btnPlayA.Click += (_, _) => PlayCurrentTrackA();

        btnPlayB = MakeGhostButton("Play B", 76);
        btnPlayB.Margin = new Padding(0, 0, 16, 0);
        btnPlayB.Click += (_, _) => PlayCurrentTrackB();

        btnBeginA = MakePrimaryButton("Begin Track A", 130);
        btnBeginA.Margin = new Padding(0, 0, 6, 0);
        btnBeginA.Click += (_, _) => BeginTrackA();

        btnBeginB = MakePrimaryButton("Begin Track B", 130);
        btnBeginB.Margin = new Padding(0, 0, 6, 0);
        btnBeginB.Click += (_, _) => BeginTrackB();

        btnOpenVoting = MakePrimaryButton("Open Voting", 116);
        btnOpenVoting.Margin = new Padding(0, 0, 6, 0);
        btnOpenVoting.Click += (_, _) => ExecutePhase(() => controller!.OpenPrimaryVoting());

        btnReveal = MakePrimaryButton("Reveal Result", 126);
        btnReveal.Margin = new Padding(0, 0, 16, 0);
        btnReveal.Click += (_, _) => RevealCurrent();

        btnSkip = MakeGhostButton("Skip", 72);
        btnSkip.Margin = new Padding(0, 0, 6, 0);
        btnSkip.Click += (_, _) => SkipCurrentMatch();

        btnPauseResume = MakeGhostButton("Pause", 80);
        btnPauseResume.Click += (_, _) => TogglePause();

        playbackRow.Controls.AddRange([btnPlayA, btnPlayB]);
        phaseRow.Controls.AddRange([btnBeginA, btnBeginB, btnOpenVoting, btnReveal, btnSkip, btnPauseResume]);
        controlsRow.Controls.Add(playbackRow, 0, 0);
        controlsRow.Controls.Add(phaseRow, 0, 1);

        // Row 3: vote section
        voteSection = new Panel { BackColor = p.SurfaceBackColor, Dock = DockStyle.Fill };
        judgeVoteRows = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(20, 8, 20, 8),
            WrapContents = false
        };
        judgeVoteRows.Resize += (_, _) =>
        {
            var newWidth = judgeVoteRows.Width - 40;
            if (newWidth <= 0) return;
            foreach (Control row in judgeVoteRows.Controls)
                row.Width = newWidth;
        };
        lblTally = MakeLabel("", 9f, color: p.TextSecondaryColor);
        lblTally.Dock = DockStyle.Bottom;
        lblTally.Padding = new Padding(20, 4, 20, 8);
        voteSection.Controls.Add(judgeVoteRows);
        voteSection.Controls.Add(lblTally);

        // Row 4: result section
        resultSection = new Panel { BackColor = p.SurfaceBackColor, Dock = DockStyle.Fill };
        var resultInner = new TableLayoutPanel
        {
            ColumnCount = 2,
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 18, 28, 18),
            RowCount = 2
        };
        resultInner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        resultInner.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        resultInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resultInner.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        lblOutcomeTitle = MakeLabel("", 16f, bold: true, color: p.AccentPrimaryColor);
        lblOutcomeDetail = MakeLabel("", 10f, color: p.TextSecondaryColor);
        lblOutcomeDetail.Margin = new Padding(0, 4, 0, 0);

        btnNextMatch = MakePrimaryButton("Next Match >", 150);
        btnNextMatch.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        btnNextMatch.Click += (_, _) => AdvanceToNextMatch();

        resultInner.Controls.Add(lblOutcomeTitle, 0, 0);
        resultInner.Controls.Add(btnNextMatch, 1, 0);
        resultInner.Controls.Add(lblOutcomeDetail, 0, 1);
        resultSection.Controls.Add(resultInner);

        // Row 5: bracket + match log
        hostTabs = new TabControl
        {
            Dock = DockStyle.Fill,
            Appearance = TabAppearance.Normal,
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextPrimaryColor,
            Margin = Padding.Empty
        };

        var bracketPage = new TabPage("Bracket")
        {
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextPrimaryColor,
            Padding = new Padding(0)
        };
        bracketView = new SongWarsBracketView(p)
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        bracketPage.Controls.Add(bracketView);

        var logPage = new TabPage("Match Log")
        {
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextPrimaryColor,
            Padding = new Padding(0)
        };

        lstLog = new ListView
        {
            Dock = DockStyle.Fill,
            FullRowSelect = true,
            GridLines = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            MultiSelect = false,
            View = View.Details,
            BackColor = p.SurfaceBackColor,
            ForeColor = p.TextSecondaryColor,
            BorderStyle = BorderStyle.None
        };
        lstLog.Columns.Add("Round", 140);
        lstLog.Columns.Add("Track A", 220);
        lstLog.Columns.Add("Track B", 220);
        lstLog.Columns.Add("Result", 100);
        lstLog.Columns.Add("Winner", 220);
        logPage.Controls.Add(lstLog);
        hostTabs.TabPages.Add(bracketPage);
        hostTabs.TabPages.Add(logPage);

        hostRoot.Controls.Add(header, 0, 0);
        hostRoot.Controls.Add(matchCard, 0, 1);
        hostRoot.Controls.Add(controlsRow, 0, 2);
        hostRoot.Controls.Add(voteSection, 0, 3);
        hostRoot.Controls.Add(resultSection, 0, 4);
        hostRoot.Controls.Add(hostTabs, 0, 5);

        var wrapper = new Panel { Dock = DockStyle.Fill, Visible = false };
        wrapper.Controls.Add(hostRoot);
        wrapper.Resize += (_, _) => UpdateResponsiveHostLayout();
        return wrapper;
    }

    private void UpdateResponsiveHostLayout()
    {
        if (hostRoot is null || hostRoot.RowStyles.Count < 6)
            return;

        var height = hostRoot.ClientSize.Height;
        var narrow = hostRoot.ClientSize.Width < 860;
        var compact = height < 650;

        hostRoot.SuspendLayout();
        hostRoot.RowStyles[0] = new RowStyle(SizeType.Absolute, compact ? 70F : 80F);
        hostRoot.RowStyles[1] = new RowStyle(SizeType.Absolute, compact ? 118F : 152F);
        hostRoot.RowStyles[2] = new RowStyle(SizeType.Absolute, narrow ? 112F : 104F);
        hostRoot.ResumeLayout(true);

        if (lblTrackATitle is not null)
            lblTrackATitle.Font = new Font("Segoe UI Semibold", compact ? 11.5f : 13f, FontStyle.Bold, GraphicsUnit.Point);
        if (lblTrackBTitle is not null)
            lblTrackBTitle.Font = new Font("Segoe UI Semibold", compact ? 11.5f : 13f, FontStyle.Bold, GraphicsUnit.Point);

        hostTabs?.Invalidate();
        ApplyDynamicRows();
    }

    // ── Host panel refresh ─────────────────────────────────────────────────

    private void RefreshHostPanel()
    {
        if (controller is null) return;

        var t = controller.Tournament;
        var match = controller.CurrentMatch;

        // Header
        lblTournamentHeader.Text = t.Name;
        var totalMatches = t.Matches.Count(m => m.Result != SongWarsOutcome.Pending);
        lblMatchStatus.Text = $"Matches: {totalMatches}/{t.Matches.Count}   Eliminations: {t.EliminationCount}/{SongWarsVoteTally.MaxDirectEliminations}";

        if (pendingResultMatchId is not null)
        {
            var resultMatch = t.Matches.FirstOrDefault(m =>
                string.Equals(m.MatchId, pendingResultMatchId, StringComparison.OrdinalIgnoreCase));
            if (resultMatch is not null)
            {
                ShowMatch(resultMatch);
                lblRoundStatus.Text = $"{BracketLabel(resultMatch.Bracket)}  |  {resultMatch.RoundId}  |  Result";
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: false, reveal: false, skip: false, pause: false);
                PopulateResultSection(resultMatch);
                SetDynamicRows(0, ResultSectionHeight);
                RefreshMatchLog();
                bracketView.SetTournament(t, resultMatch.MatchId);
                return;
            }

            pendingResultMatchId = null;
        }

        if (match is null)
        {
            // Tournament complete
            var winner = t.Submissions.FirstOrDefault(s => s.Status is SongWarsSubmissionStatus.Active or SongWarsSubmissionStatus.LosersBracket);
            lblRoundStatus.Text = winner is not null
                ? $"Tournament Complete  |  Winner: {winner.DisplayTitle}"
                : "Tournament Complete";
            lblTrackATitle.Text = "-";
            lblTrackAArtist.Text = "";
            lblTrackBTitle.Text = "-";
            lblTrackBArtist.Text = "";
            lblFocusIndicator.Text = "";
            btnPlayA.Enabled = false;
            btnPlayB.Enabled = false;

            SetPhaseButtonStates(false, false, false, false, false, false);
            SetDynamicRows(0, 0);
        }
        else
        {
            // Active match
            lblRoundStatus.Text = $"{BracketLabel(match.Bracket)}  |  {match.RoundId}  |  Phase: {FriendlyPhase(match.Phase)}";
            ShowMatch(match);

            UpdatePhaseControls(match);
        }

        RefreshMatchLog();
        bracketView.SetTournament(t, pendingResultMatchId ?? match?.MatchId);
    }

    private static string BracketLabel(SongWarsBracket bracket) => bracket switch
    {
        SongWarsBracket.Winners => "Winners Bracket",
        SongWarsBracket.Losers => "Losers Bracket",
        SongWarsBracket.GrandFinals => "Grand Finals",
        _ => bracket.ToString()
    };

    private static string FriendlyPhase(SongWarsMatchPhase phase) => phase switch
    {
        SongWarsMatchPhase.Pending => "Queued",
        SongWarsMatchPhase.Ready => "Ready",
        SongWarsMatchPhase.TrackAPlaying => "Track A playing",
        SongWarsMatchPhase.TrackBPlaying => "Track B playing",
        SongWarsMatchPhase.PrimaryVoting => "Voting open",
        SongWarsMatchPhase.EliminationVoting => "Elimination vote",
        SongWarsMatchPhase.Reveal => "Revealed",
        SongWarsMatchPhase.Complete => "Complete",
        SongWarsMatchPhase.Skipped => "Skipped",
        SongWarsMatchPhase.Paused => "Paused",
        _ => phase.ToString()
    };

    private void ShowMatch(SongWarsMatch match)
    {
        if (controller is null) return;

        var trackA = FindSubmission(match.SlotASubmissionId);
        var trackB = FindSubmission(match.SlotBSubmissionId);
        lblTrackATitle.Text = trackA?.DisplayTitle ?? "-";
        lblTrackAArtist.Text = trackA?.ArtistDisplayName ?? "";
        lblTrackBTitle.Text = trackB?.DisplayTitle ?? "-";
        lblTrackBArtist.Text = trackB?.ArtistDisplayName ?? "";
        lblFocusIndicator.Text = match.FocusSlot == SongWarsMatchSlot.B ? "On trial: Track B" : "On trial: Track A";
        btnPlayA.Enabled = !string.IsNullOrWhiteSpace(trackA?.LocalFilePath);
        btnPlayB.Enabled = !string.IsNullOrWhiteSpace(trackB?.LocalFilePath);
    }

    private SongWarsSubmission? FindSubmission(string? submissionId) =>
        string.IsNullOrWhiteSpace(submissionId) || controller is null
            ? null
            : controller.Tournament.Submissions.FirstOrDefault(s =>
                string.Equals(s.SubmissionId, submissionId, StringComparison.OrdinalIgnoreCase));

    private void UpdatePhaseControls(SongWarsMatch match)
    {
        switch (match.Phase)
        {
            case SongWarsMatchPhase.Pending:
            case SongWarsMatchPhase.Ready:
                SetPhaseButtonStates(beginA: true, beginB: false, openVoting: false, reveal: false, skip: true, pause: false);
                SetDynamicRows(0, 0);
                break;

            case SongWarsMatchPhase.TrackAPlaying:
                SetPhaseButtonStates(beginA: false, beginB: true, openVoting: false, reveal: false, skip: true, pause: true);
                SetDynamicRows(0, 0);
                break;

            case SongWarsMatchPhase.TrackBPlaying:
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: true, reveal: false, skip: true, pause: true);
                SetDynamicRows(0, 0);
                break;

            case SongWarsMatchPhase.PrimaryVoting:
            case SongWarsMatchPhase.EliminationVoting:
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: false, reveal: true, skip: false, pause: false);
                RebuildVoteRows(match);
                SetDynamicRows(VoteSectionBaseHeight + controller!.Tournament.Judges.Count * VoteRowHeight, 0);
                break;

            case SongWarsMatchPhase.Reveal:
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: false, reveal: false, skip: false, pause: false);
                PopulateResultSection(match);
                SetDynamicRows(0, ResultSectionHeight);
                break;

            case SongWarsMatchPhase.Complete:
            case SongWarsMatchPhase.Skipped:
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: false, reveal: false, skip: false, pause: false);
                SetDynamicRows(0, 0);
                break;

            case SongWarsMatchPhase.Paused:
                SetPhaseButtonStates(beginA: false, beginB: false, openVoting: false, reveal: false, skip: true, pause: true);
                SetDynamicRows(0, 0);
                btnPauseResume.Text = "Resume";
                break;
        }

        if (match.Phase != SongWarsMatchPhase.Paused)
            btnPauseResume.Text = "Pause";
    }

    private void SetPhaseButtonStates(bool beginA, bool beginB, bool openVoting, bool reveal, bool skip, bool pause)
    {
        btnBeginA.Enabled = beginA;
        btnBeginB.Enabled = beginB;
        btnOpenVoting.Enabled = openVoting;
        btnReveal.Enabled = reveal;
        btnSkip.Enabled = skip;
        btnPauseResume.Enabled = pause;
    }

    private void SetDynamicRows(int voteHeight, int resultHeight)
    {
        requestedVoteHeight = voteHeight;
        requestedResultHeight = resultHeight;
        ApplyDynamicRows();
    }

    private void ApplyDynamicRows()
    {
        if (hostRoot is null || hostRoot.RowStyles.Count < 5)
            return;

        var staticHeight = 0f;
        for (var i = 0; i <= 2; i++)
            staticHeight += hostRoot.RowStyles[i].Height;

        var tabMinimumHeight = hostRoot.ClientSize.Height < 650 ? 120 : 180;
        var availableDynamicHeight = Math.Max(0, hostRoot.ClientSize.Height - (int)staticHeight - tabMinimumHeight);
        var resultHeight = Math.Min(requestedResultHeight, availableDynamicHeight);
        var voteHeight = Math.Min(requestedVoteHeight, Math.Max(0, availableDynamicHeight - resultHeight));

        hostRoot.SuspendLayout();
        hostRoot.RowStyles[3] = new RowStyle(SizeType.Absolute, voteHeight);
        hostRoot.RowStyles[4] = new RowStyle(SizeType.Absolute, resultHeight);
        voteSection.Visible = voteHeight > 0;
        resultSection.Visible = resultHeight > 0;
        hostRoot.ResumeLayout(true);
    }

    private void RebuildVoteRows(SongWarsMatch match)
    {
        judgeVoteRows.Controls.Clear();
        if (controller is null) return;

        var votes = controller.GetVotes(match.MatchId);
        var phase = match.Phase == SongWarsMatchPhase.EliminationVoting
            ? SongWarsVotePhase.Elimination
            : SongWarsVotePhase.Primary;

        foreach (var judge in controller.Tournament.Judges)
        {
            var latestVote = votes
                .Where(v => string.Equals(v.JudgeId, judge.JudgeId, StringComparison.OrdinalIgnoreCase) && v.Phase == phase)
                .OrderByDescending(v => v.Revision)
                .FirstOrDefault();

            var row = new FlowLayoutPanel
            {
                AutoSize = false,
                FlowDirection = FlowDirection.LeftToRight,
                Height = VoteRowHeight,
                WrapContents = false,
                Width = judgeVoteRows.Width > 0 ? judgeVoteRows.Width - 40 : 860
            };

            var nameLbl = MakeLabel(judge.DisplayName, 9.5f);
            nameLbl.AutoSize = false;
            nameLbl.TextAlign = ContentAlignment.MiddleLeft;
            nameLbl.Width = 140;
            nameLbl.Height = 36;
            nameLbl.Margin = new Padding(0, 6, 12, 0);

            var judgeId = judge.JudgeId;
            var currentPhase = phase;

            var aWinsSelected = latestVote?.Choice != SongWarsVoteChoice.Eliminated &&
                                latestVote?.SlotAChoice == SongWarsTrackVoteChoice.Pass &&
                                latestVote?.SlotBChoice == SongWarsTrackVoteChoice.Fail;
            var bWinsSelected = latestVote?.Choice != SongWarsVoteChoice.Eliminated &&
                                latestVote?.SlotBChoice == SongWarsTrackVoteChoice.Pass &&
                                latestVote?.SlotAChoice == SongWarsTrackVoteChoice.Fail;

            var btnAWins = MakeVoteButton("A Wins", 86, p.DangerColor, aWinsSelected);
            btnAWins.Margin = new Padding(0, 4, 6, 0);
            btnAWins.Click += (_, _) => SubmitTrackVoteAndRefresh(judgeId, SongWarsMatchSlot.A, SongWarsTrackVoteChoice.Pass, currentPhase);

            var btnBWins = MakeVoteButton("B Wins", 86, p.AccentPrimaryColor, bWinsSelected);
            btnBWins.Margin = new Padding(0, 4, 6, 0);
            btnBWins.Click += (_, _) => SubmitTrackVoteAndRefresh(judgeId, SongWarsMatchSlot.B, SongWarsTrackVoteChoice.Pass, currentPhase);

            row.Controls.Add(nameLbl);
            row.Controls.Add(btnAWins);
            row.Controls.Add(btnBWins);

            var btnAEliminated = MakeVoteButton("A Eliminated", 112, p.DangerColor, IsEliminationVoteForSlot(latestVote, SongWarsMatchSlot.A));
            btnAEliminated.Margin = new Padding(0, 4, 6, 0);
            btnAEliminated.Enabled = match.Bracket == SongWarsBracket.Winners;
            btnAEliminated.Click += (_, _) => SubmitEliminationVoteAndRefresh(judgeId, SongWarsMatchSlot.A, currentPhase);

            var btnBEliminated = MakeVoteButton("B Eliminated", 112, p.DangerColor, IsEliminationVoteForSlot(latestVote, SongWarsMatchSlot.B));
            btnBEliminated.Margin = new Padding(0, 4, 12, 0);
            btnBEliminated.Enabled = match.Bracket == SongWarsBracket.Winners;
            btnBEliminated.Click += (_, _) => SubmitEliminationVoteAndRefresh(judgeId, SongWarsMatchSlot.B, currentPhase);

            row.Controls.Add(btnAEliminated);
            row.Controls.Add(btnBEliminated);

            // Current vote indicator
            var voteLbl = latestVote is null
                ? MakeLabel("not voted", 9f, color: p.TextMutedColor)
                : MakeLabel(DescribeVote(latestVote), 9f, color: VoteColor(latestVote.Choice));
            voteLbl.AutoSize = false;
            voteLbl.TextAlign = ContentAlignment.MiddleLeft;
            voteLbl.Width = 130;
            voteLbl.Height = 36;
            voteLbl.Margin = new Padding(0, 4, 0, 0);
            row.Controls.Add(voteLbl);

            judgeVoteRows.Controls.Add(row);
        }

        UpdateTallyLabel(match, phase);
    }

    private Color VoteColor(SongWarsVoteChoice choice) => choice switch
    {
        SongWarsVoteChoice.Pass => p.AccentPrimaryColor,
        SongWarsVoteChoice.Fail => p.DangerColor,
        SongWarsVoteChoice.Eliminated => p.DangerColor,
        _ => p.TextMutedColor
    };

    private ModernButton MakeVoteButton(string text, int width, Color accent, bool selected)
    {
        if (!selected)
            return MakeGhostButton(text, width, accent);

        var btn = new ModernButton
        {
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point),
            Size = new Size(width, 36),
            Text = text
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btn, p, accent);
        return btn;
    }

    private static bool IsEliminationVoteForSlot(SongWarsVote? vote, SongWarsMatchSlot slot)
    {
        if (vote?.Choice != SongWarsVoteChoice.Eliminated)
            return false;

        return slot == SongWarsMatchSlot.A
            ? vote.SlotAChoice == SongWarsTrackVoteChoice.Fail && vote.SlotBChoice == SongWarsTrackVoteChoice.Pass
            : vote.SlotBChoice == SongWarsTrackVoteChoice.Fail && vote.SlotAChoice == SongWarsTrackVoteChoice.Pass;
    }

    private static string DescribeVote(SongWarsVote vote)
    {
        if (vote.Choice == SongWarsVoteChoice.Eliminated)
        {
            if (IsEliminationVoteForSlot(vote, SongWarsMatchSlot.A))
                return "A eliminated";
            if (IsEliminationVoteForSlot(vote, SongWarsMatchSlot.B))
                return "B eliminated";
            return "eliminated";
        }

        if (vote.SlotAChoice == SongWarsTrackVoteChoice.Pass && vote.SlotBChoice == SongWarsTrackVoteChoice.Fail)
            return "A wins";

        if (vote.SlotBChoice == SongWarsTrackVoteChoice.Pass && vote.SlotAChoice == SongWarsTrackVoteChoice.Fail)
            return "B wins";

        return vote.Choice.ToString();
    }

    private void UpdateTallyLabel(SongWarsMatch match, SongWarsVotePhase phase)
    {
        if (controller is null) return;

        var votes = controller.GetVotes(match.MatchId);
        var latest = votes
            .Where(v => v.Phase == phase)
            .GroupBy(v => v.JudgeId, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(v => v.Revision).First())
            .ToList();

        var pass = latest.Count(v => v.Choice == SongWarsVoteChoice.Pass);
        var fail = latest.Count(v => v.Choice == SongWarsVoteChoice.Fail);
        var aElim = latest.Count(v => IsEliminationVoteForSlot(v, SongWarsMatchSlot.A));
        var bElim = latest.Count(v => IsEliminationVoteForSlot(v, SongWarsMatchSlot.B));
        var pending = controller.Tournament.Judges.Count - latest.Count;

        lblTally.Text = $"B wins: {pass}  |  A wins: {fail}  |  A eliminated: {aElim}  |  B eliminated: {bElim}  |  Pending: {pending}";
    }

    private void PopulateResultSection(SongWarsMatch match)
    {
        var snapshot = match.VoteSnapshots.LastOrDefault();
        var t = controller!.Tournament;

        var outcome = snapshot?.Outcome ?? match.Result;
        lblOutcomeTitle.Text = outcome switch
        {
            SongWarsOutcome.Pass => match.FocusSlot == SongWarsMatchSlot.A ? "A WINS" : "B WINS",
            SongWarsOutcome.Fail => match.FocusSlot == SongWarsMatchSlot.A ? "B WINS" : "A WINS",
            SongWarsOutcome.Eliminated => snapshot?.EliminatedSlot == SongWarsMatchSlot.A
                ? "A ELIMINATED"
                : snapshot?.EliminatedSlot == SongWarsMatchSlot.B
                    ? "B ELIMINATED"
                    : "ELIMINATED",
            SongWarsOutcome.Skip => "SKIP",
            _ => outcome.ToString()
        };

        lblOutcomeTitle.ForeColor = outcome switch
        {
            SongWarsOutcome.Pass => p.AccentPrimaryColor,
            SongWarsOutcome.Eliminated => p.DangerColor,
            SongWarsOutcome.Skip => p.TextMutedColor,
            _ => p.DangerColor
        };

        var winnerName = match.WinnerSubmissionId is { } wId
            ? t.Submissions.FirstOrDefault(s => string.Equals(s.SubmissionId, wId, StringComparison.OrdinalIgnoreCase))?.DisplayTitle
            : null;

        var explanation = snapshot?.Explanation ?? "This match was skipped and requeued for later in the round.";
        lblOutcomeDetail.Text = explanation
            + (winnerName is not null ? $"  -  {winnerName} advances." : "");

        btnNextMatch.Text = t.CurrentMatchId is not null ? "Next Match >" : "View Results";
    }

    private void RefreshMatchLog()
    {
        if (controller is null) return;

        lstLog.Items.Clear();
        var t = controller.Tournament;
        var completed = t.Matches
            .Where(m => m.Result != SongWarsOutcome.Pending)
            .OrderBy(m => m.CompletedAtUtc ?? DateTimeOffset.MaxValue)
            .ToList();

        foreach (var m in completed)
        {
            var subA = t.Submissions.FirstOrDefault(s => string.Equals(s.SubmissionId, m.SlotASubmissionId, StringComparison.OrdinalIgnoreCase));
            var subB = t.Submissions.FirstOrDefault(s => string.Equals(s.SubmissionId, m.SlotBSubmissionId, StringComparison.OrdinalIgnoreCase));
            var winnerSub = m.WinnerSubmissionId is { } wId
                ? t.Submissions.FirstOrDefault(s => string.Equals(s.SubmissionId, wId, StringComparison.OrdinalIgnoreCase))
                : null;

            var item = new ListViewItem(m.RoundId);
            item.SubItems.Add(subA?.DisplayTitle ?? m.SlotASubmissionId);
            item.SubItems.Add(subB?.DisplayTitle ?? m.SlotBSubmissionId);
            item.SubItems.Add(m.Result.ToString());
            item.SubItems.Add(winnerSub?.DisplayTitle ?? "");
            lstLog.Items.Add(item);
        }

        if (lstLog.Items.Count > 0)
            lstLog.EnsureVisible(lstLog.Items.Count - 1);
    }

    // ── Host actions ───────────────────────────────────────────────────────

    private void PlayCurrentTrackA()
    {
        var match = GetDisplayedMatch();
        var track = FindSubmission(match?.SlotASubmissionId);
        if (track?.LocalFilePath is { } path)
            playTrack(path);
    }

    private void PlayCurrentTrackB()
    {
        var match = GetDisplayedMatch();
        var track = FindSubmission(match?.SlotBSubmissionId);
        if (track?.LocalFilePath is { } path)
            playTrack(path);
    }

    private void BeginTrackA()
    {
        if (controller is null) return;
        try
        {
            controller.BeginTrackA();
            SaveCurrent();
            PlayCurrentTrackA();
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BeginTrackB()
    {
        if (controller is null) return;
        try
        {
            controller.BeginTrackB();
            SaveCurrent();
            PlayCurrentTrackB();
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private SongWarsMatch? GetDisplayedMatch()
    {
        if (controller is null) return null;
        if (pendingResultMatchId is not null)
        {
            var resultMatch = controller.Tournament.Matches.FirstOrDefault(m =>
                string.Equals(m.MatchId, pendingResultMatchId, StringComparison.OrdinalIgnoreCase));
            if (resultMatch is not null)
                return resultMatch;
        }

        return controller.CurrentMatch;
    }

    private void ExecutePhase(Action action)
    {
        if (controller is null) return;
        try
        {
            action();
            SaveCurrent();
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SubmitEliminationVoteAndRefresh(
        string judgeId,
        SongWarsMatchSlot eliminatedSlot,
        SongWarsVotePhase phase)
    {
        if (controller is null) return;
        try
        {
            controller.SubmitEliminationVote(judgeId, eliminatedSlot, phase);
            SaveVotes();

            var match = controller.CurrentMatch;
            if (match is not null)
                RebuildVoteRows(match);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SubmitTrackVoteAndRefresh(
        string judgeId,
        SongWarsMatchSlot slot,
        SongWarsTrackVoteChoice choice,
        SongWarsVotePhase phase)
    {
        if (controller is null) return;
        try
        {
            controller.SubmitTrackVote(judgeId, slot, choice, phase);
            SaveVotes();

            var match = controller.CurrentMatch;
            if (match is not null)
                RebuildVoteRows(match);
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RevealCurrent()
    {
        if (controller is null) return;
        var current = controller.CurrentMatch;
        var matchId = current?.MatchId;
        if (current is not null && HasPendingVotes(current))
        {
            var confirm = MessageBox.Show(
                this,
                "Some judges have not voted. Reveal now and apply the timer-expired rule?",
                "Reveal Result",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (confirm != DialogResult.Yes)
                return;
        }

        try
        {
            var result = controller.RevealCurrent(timerExpired: true);
            if (result.Outcome == SongWarsOutcome.Pending)
            {
                MessageBox.Show(this, "Voting is still open and no threshold has been reached.", "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveCurrent();
            SaveVotes(matchId);
            pendingResultMatchId = matchId;
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private bool HasPendingVotes(SongWarsMatch match)
    {
        if (controller is null) return false;
        var phase = match.Phase == SongWarsMatchPhase.EliminationVoting
            ? SongWarsVotePhase.Elimination
            : SongWarsVotePhase.Primary;
        var latestJudgeCount = controller.GetVotes(match.MatchId)
            .Where(v => v.Phase == phase)
            .GroupBy(v => v.JudgeId, StringComparer.OrdinalIgnoreCase)
            .Count();

        return latestJudgeCount < controller.Tournament.Judges.Count;
    }

    private void SkipCurrentMatch()
    {
        if (controller is null) return;
        var match = controller.CurrentMatch;
        if (match is null) return;

        var result = MessageBox.Show(
            this,
            "Skip this match? It will be replayed at the end of the current round.",
            "Skip Match",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes) return;

        try
        {
            SongWarsBracketEngine.AdvanceMatch(controller.Tournament, match.MatchId, SongWarsOutcome.Skip);
            SaveCurrent();
            pendingResultMatchId = match.MatchId;
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void TogglePause()
    {
        if (controller is null) return;
        var match = controller.CurrentMatch;
        if (match is null) return;

        try
        {
            if (match.Phase == SongWarsMatchPhase.Paused)
            {
                controller.Resume();
            }
            else
            {
                controller.Pause();
            }

            SaveCurrent();
            RefreshHostPanel();
        }
        catch (InvalidOperationException ex)
        {
            MessageBox.Show(this, ex.Message, "Song Wars", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void AdvanceToNextMatch()
    {
        if (controller is null) return;
        pendingResultMatchId = null;

        if (controller.Tournament.CurrentMatchId is null)
        {
            // Tournament complete - show final summary
            MessageBox.Show(
                this,
                BuildWinnerMessage(),
                "Song Wars - Tournament Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        // Advance the current queued match to Ready.
        var match = controller.CurrentMatch;
        if (match is { Phase: SongWarsMatchPhase.Pending })
        {
            controller.Start();
            SaveCurrent();
        }

        playTrack(null);
        RefreshHostPanel();
    }

    private string BuildWinnerMessage()
    {
        var winner = controller?.Tournament.Submissions
            .FirstOrDefault(s => s.Status is SongWarsSubmissionStatus.Active or SongWarsSubmissionStatus.LosersBracket);

        return winner is not null
            ? $"Song Wars complete!\n\nWinner: {winner.DisplayTitle}\nby {winner.ArtistDisplayName}"
            : "Song Wars complete!";
    }

    // ── Persistence helpers ────────────────────────────────────────────────

    private void SaveCurrent()
    {
        if (controller is null) return;
        _ = store.SaveTournamentAsync(controller.Tournament);
    }

    private void SaveVotes(string? matchId = null)
    {
        if (controller is null) return;
        matchId ??= controller.CurrentMatch?.MatchId;
        if (string.IsNullOrWhiteSpace(matchId)) return;
        var votes = controller.GetVotes(matchId);
        _ = store.SaveVotesAsync(controller.Tournament.TournamentId, matchId, votes);
    }

    // ── Control factory helpers ────────────────────────────────────────────

    private Label MakeLabel(string text, float fontSize = 9.5f, bool bold = false, Color? color = null) =>
        new()
        {
            AutoSize = true,
            Font = new Font(
                bold ? "Segoe UI Semibold" : "Segoe UI",
                fontSize,
                bold ? FontStyle.Bold : FontStyle.Regular,
                GraphicsUnit.Point),
            ForeColor = color ?? p.TextPrimaryColor,
            Text = text,
            UseMnemonic = false
        };

    private ModernButton MakeGhostButton(string text, int width, Color? accent = null)
    {
        var btn = new ModernButton
        {
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point),
            Size = new Size(width, 36),
            Text = text
        };
        ThemeControlStyler.ApplyGhostButtonTheme(btn, p, accent ?? p.AccentSoftColor);
        return btn;
    }

    private ModernButton MakePrimaryButton(string text, int width)
    {
        var btn = new ModernButton
        {
            Font = new Font("Segoe UI Semibold", 8.75f, FontStyle.Bold, GraphicsUnit.Point),
            Size = new Size(width, 36),
            Text = text
        };
        ThemeControlStyler.ApplyPrimaryButtonTheme(btn, p, p.AccentPrimaryColor);
        return btn;
    }
}
