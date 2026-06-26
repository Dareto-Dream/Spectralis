using System.Drawing;

namespace Spectralis;

internal sealed class SmartPlaylistEditorDialog : Form
{
    private readonly SmartPlaylist _playlist;
    private readonly List<RuleRow> _rows = [];

    public SmartPlaylist Result => _playlist;

    private readonly TextBox       txtName     = new();
    private readonly ComboBox      cmbMatch    = new();
    private readonly NumericUpDown numLimit    = new();
    private readonly ComboBox      cmbSortBy   = new();
    private readonly ComboBox      cmbSortDir  = new();
    private readonly FlowLayoutPanel rulesPanel = new();
    private readonly Button        btnAddRule  = new();
    private readonly Button        btnSave     = new();
    private readonly Button        btnCancel   = new();

    public SmartPlaylistEditorDialog(SmartPlaylist playlist, ThemePalette theme)
    {
        _playlist = playlist;

        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode       = AutoScaleMode.Font;
        Text                = "Edit Smart Playlist";
        Size                = new Size(580, 500);
        MinimumSize         = new Size(500, 400);
        FormBorderStyle     = FormBorderStyle.Sizable;
        MaximizeBox         = false;
        StartPosition       = FormStartPosition.CenterParent;

        BuildLayout();
        PopulateFromPlaylist();
        ApplyTheme(theme);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Layout
    // ─────────────────────────────────────────────────────────────────────────

    private void BuildLayout()
    {
        SuspendLayout();

        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 5,
            Padding     = new Padding(12, 10, 12, 8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // name
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // match options
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // rules panel (scrollable)
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));  // sort options
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));  // bottom bar

        // ── Row 0: Name ──────────────────────────────────────────────────────
        var nameRow = BuildLabeledRow("Name", txtName);
        txtName.Font = new Font("Segoe UI", 10f);
        root.Controls.Add(nameRow, 0, 0);

        // ── Row 1: Match + Limit ─────────────────────────────────────────────
        var matchRow = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents  = false,
        };
        var lblMatch = new Label { Text = "Match", AutoSize = true, Font = new Font("Segoe UI", 9f), Padding = new Padding(0, 7, 4, 0) };
        cmbMatch.DropDownStyle  = ComboBoxStyle.DropDownList;
        cmbMatch.Font           = new Font("Segoe UI", 9f);
        cmbMatch.Width          = 56;
        cmbMatch.Items.AddRange(["All", "Any"]);
        cmbMatch.SelectedIndex  = 0;
        var lblRules = new Label { Text = "rules  |  Limit:", AutoSize = true, Font = new Font("Segoe UI", 9f), Padding = new Padding(4, 7, 4, 0) };
        numLimit.Minimum = 0;
        numLimit.Maximum = 9999;
        numLimit.Width   = 60;
        numLimit.Font    = new Font("Segoe UI", 9f);
        var lblUnlimited = new Label { Text = "(0 = unlimited)", AutoSize = true, Font = new Font("Segoe UI", 8.5f), ForeColor = Color.Gray, Padding = new Padding(4, 8, 0, 0) };
        matchRow.Controls.AddRange([lblMatch, cmbMatch, lblRules, numLimit, lblUnlimited]);
        root.Controls.Add(matchRow, 0, 1);

        // ── Row 2: Rules ─────────────────────────────────────────────────────
        var rulesScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        rulesPanel.Dock          = DockStyle.Top;
        rulesPanel.AutoSize      = true;
        rulesPanel.FlowDirection = FlowDirection.TopDown;
        rulesPanel.WrapContents  = false;
        rulesScroll.Controls.Add(rulesPanel);
        root.Controls.Add(rulesScroll, 0, 2);

        // ── Row 3: Sort ──────────────────────────────────────────────────────
        var sortRow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        var lblSort = new Label { Text = "Sort by", AutoSize = true, Font = new Font("Segoe UI", 9f), Padding = new Padding(0, 7, 4, 0) };
        cmbSortBy.DropDownStyle  = ComboBoxStyle.DropDownList;
        cmbSortBy.Font           = new Font("Segoe UI", 9f);
        cmbSortBy.Width          = 110;
        cmbSortBy.Items.AddRange(["Date Added", "Title", "Artist", "Album", "Year", "Play Count", "Duration", "Last Played"]);
        cmbSortBy.SelectedIndex  = 0;
        cmbSortDir.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSortDir.Font          = new Font("Segoe UI", 9f);
        cmbSortDir.Width         = 90;
        cmbSortDir.Items.AddRange(["Descending", "Ascending"]);
        cmbSortDir.SelectedIndex = 0;
        sortRow.Controls.AddRange([lblSort, cmbSortBy, cmbSortDir]);
        root.Controls.Add(sortRow, 0, 3);

        // ── Row 4: Bottom ────────────────────────────────────────────────────
        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));   // Add Rule
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        btnAddRule.Text      = "+ Add Rule";
        btnAddRule.Size      = new Size(96, 30);
        btnAddRule.FlatStyle = FlatStyle.Flat;
        btnAddRule.Font      = new Font("Segoe UI", 9f);
        btnAddRule.Anchor    = AnchorStyles.Left | AnchorStyles.Top;
        btnAddRule.Click    += (_, _) => AddRuleRow(new SmartRule());

        btnSave.Text   = "Save";
        btnCancel.Text = "Cancel";
        foreach (var b in new[] { btnSave, btnCancel })
        {
            b.Size      = new Size(80, 30);
            b.FlatStyle = FlatStyle.Flat;
            b.Font      = new Font("Segoe UI Semibold", 9f, FontStyle.Bold);
            b.Anchor    = AnchorStyles.None;
        }
        btnSave.Click   += BtnSave_Click;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };

        bottom.Controls.Add(btnAddRule, 0, 0);
        bottom.Controls.Add(new Panel(), 1, 0);
        bottom.Controls.Add(btnSave,    2, 0);
        bottom.Controls.Add(btnCancel,  3, 0);
        root.Controls.Add(bottom, 0, 4);

        Controls.Add(root);
        ResumeLayout(false);
    }

    private static Panel BuildLabeledRow(string labelText, Control ctrl)
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        var lbl   = new Label
        {
            Text      = labelText,
            Location  = new Point(0, 8),
            AutoSize  = true,
            Font      = new Font("Segoe UI", 9f),
        };
        ctrl.Dock = DockStyle.None;
        ctrl.Location = new Point(lbl.PreferredWidth + 8, 4);
        ctrl.Width    = 300;
        ctrl.Anchor   = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        panel.Controls.Add(lbl);
        panel.Controls.Add(ctrl);
        panel.Resize += (_, _) => ctrl.Width = panel.Width - lbl.PreferredWidth - 16;
        return panel;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Rule rows
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class RuleRow
    {
        public Panel      Panel    { get; }
        public ComboBox   CmbField { get; }
        public ComboBox   CmbOp    { get; }
        public TextBox    TxtValue { get; }
        public SmartRule  Rule     { get; }

        public RuleRow(SmartRule rule)
        {
            Rule = rule;
            Panel    = new Panel { Height = 32, Width = 540, Margin = new Padding(0, 2, 0, 2) };
            CmbField = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Location = new Point(0, 4), Font = new Font("Segoe UI", 9f) };
            CmbOp    = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, Location = new Point(116, 4), Font = new Font("Segoe UI", 9f) };
            TxtValue = new TextBox  { Width = 180, Location = new Point(232, 4), Font = new Font("Segoe UI", 9f) };
        }
    }

    private static readonly string[] FieldNames = ["Title", "Artist", "Album", "Alb. Artist", "Genre", "Year", "Play Count", "Duration (s)"];
    private static readonly string[] TextOps    = ["Contains", "Not Contains", "Is", "Is Not"];
    private static readonly string[] NumOps     = ["=", "≠", ">", "<"];

    private void AddRuleRow(SmartRule rule)
    {
        var row = new RuleRow(rule);
        row.CmbField.Items.AddRange(FieldNames);
        row.CmbField.SelectedIndex = (int)rule.Field;
        row.CmbField.SelectedIndexChanged += (_, _) => UpdateOpChoices(row);

        UpdateOpChoices(row);
        row.CmbOp.SelectedIndex = Math.Min((int)rule.Op, row.CmbOp.Items.Count - 1);
        row.TxtValue.Text = rule.Value;

        var btnDel = new Button
        {
            Text      = "✕",
            Size      = new Size(28, 24),
            Location  = new Point(row.TxtValue.Right + 6, 4),
            FlatStyle = FlatStyle.Flat,
            Font      = new Font("Segoe UI", 8f),
        };
        btnDel.Click += (_, _) =>
        {
            _rows.Remove(row);
            rulesPanel.Controls.Remove(row.Panel);
        };
        row.Panel.Controls.AddRange([row.CmbField, row.CmbOp, row.TxtValue, btnDel]);
        rulesPanel.Controls.Add(row.Panel);
        _rows.Add(row);
    }

    private static void UpdateOpChoices(RuleRow row)
    {
        var idx   = row.CmbField.SelectedIndex;
        var isNum = idx >= (int)SmartRuleField.Year;
        row.CmbOp.Items.Clear();
        row.CmbOp.Items.AddRange(isNum ? NumOps : TextOps);
        if (row.CmbOp.Items.Count > 0)
            row.CmbOp.SelectedIndex = 0;
    }

    private void PopulateFromPlaylist()
    {
        txtName.Text        = _playlist.Name;
        cmbMatch.SelectedIndex = (int)_playlist.Match;
        numLimit.Value      = _playlist.Limit;
        cmbSortBy.SelectedIndex  = (int)_playlist.SortBy;
        cmbSortDir.SelectedIndex = _playlist.SortDescending ? 0 : 1;

        foreach (var rule in _playlist.Rules)
            AddRuleRow(rule);
    }

    private void ApplyTheme(ThemePalette theme)
    {
        BackColor = theme.WindowBackColor;
        ForeColor = theme.TextPrimaryColor;

        txtName.BackColor   = theme.SurfaceRaisedColor;
        txtName.ForeColor   = theme.TextPrimaryColor;
        txtName.BorderStyle = BorderStyle.FixedSingle;

        btnAddRule.BackColor = theme.SurfaceRaisedColor;
        btnAddRule.ForeColor = theme.TextSecondaryColor;
        btnAddRule.FlatAppearance.BorderColor = theme.BorderStrongColor;
        btnSave.BackColor   = theme.AccentPrimaryColor;
        btnSave.ForeColor   = theme.AccentContrastColor;
        btnSave.FlatAppearance.BorderSize = 0;
        btnCancel.BackColor = theme.SurfaceRaisedColor;
        btnCancel.ForeColor = theme.TextSecondaryColor;
        btnCancel.FlatAppearance.BorderColor = theme.BorderStrongColor;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Save
    // ─────────────────────────────────────────────────────────────────────────

    private static SmartRuleOp MapTextOp(int idx) => idx switch
    {
        0 => SmartRuleOp.Contains,
        1 => SmartRuleOp.NotContains,
        2 => SmartRuleOp.Is,
        3 => SmartRuleOp.IsNot,
        _ => SmartRuleOp.Contains,
    };

    private static SmartRuleOp MapNumOp(int idx) => idx switch
    {
        0 => SmartRuleOp.Is,
        1 => SmartRuleOp.IsNot,
        2 => SmartRuleOp.GreaterThan,
        3 => SmartRuleOp.LessThan,
        _ => SmartRuleOp.Is,
    };

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = txtName.Text.Trim();
        if (string.IsNullOrEmpty(name)) name = "Smart Playlist";
        _playlist.Name      = name;
        _playlist.Match     = (SmartMatchMode)cmbMatch.SelectedIndex;
        _playlist.Limit     = (int)numLimit.Value;
        _playlist.SortBy    = (SmartSortField)cmbSortBy.SelectedIndex;
        _playlist.SortDescending = cmbSortDir.SelectedIndex == 0;

        _playlist.Rules.Clear();
        foreach (var row in _rows)
        {
            var fieldIdx = row.CmbField.SelectedIndex;
            var isNum    = fieldIdx >= (int)SmartRuleField.Year;
            var op       = isNum ? MapNumOp(row.CmbOp.SelectedIndex) : MapTextOp(row.CmbOp.SelectedIndex);
            _playlist.Rules.Add(new SmartRule
            {
                Field = (SmartRuleField)fieldIdx,
                Op    = op,
                Value = row.TxtValue.Text.Trim(),
            });
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
